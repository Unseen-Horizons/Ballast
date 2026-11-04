// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Jobs;
using Ballast.SqlServer;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Ballast.Auth;

/// <summary>What one directory-sync run came to.</summary>
public enum MicrosoftSyncOutcome
{
    /// <summary>The sync is not set up: the tenant, the client or the client secret is missing. Nothing was read.</summary>
    Off,

    /// <summary>This copy of the app is not the job holder. Nothing was read or written.</summary>
    NotHolder,

    /// <summary>Microsoft could not be read. Nothing was written.</summary>
    ReadFailed,

    /// <summary>The run would have left no linked account on. Nothing was written.</summary>
    LooksWrong,

    /// <summary>The run's changes were written in one transaction.</summary>
    Applied,

    /// <summary>The writes failed; the transaction was rolled back, so nothing was written.</summary>
    WriteFailed,
}

/// <summary>The result of one directory-sync run.</summary>
/// <param name="Outcome">What the run came to.</param>
/// <param name="Failure">Why the read failed, for <see cref="MicrosoftSyncOutcome.ReadFailed"/>; otherwise null.</param>
/// <param name="HttpStatus">The HTTP status Microsoft answered with, when the read failed with one; otherwise null.</param>
/// <param name="Counts">The counts, when the directory was read; otherwise null.</param>
/// <param name="Problems">What the owner should look at. Nothing listed was applied.</param>
/// <param name="RolesNotInMicrosoft">The roles this app created from Microsoft that no Microsoft assignment names any more. They are listed and never deleted; empty when the directory was not read.</param>
/// <param name="ClientSecretEndsUtc">When the newest client secret ends, or null when none is listed.</param>
public sealed record MicrosoftSyncResult(
    MicrosoftSyncOutcome Outcome,
    DirectorySyncFailure? Failure,
    int? HttpStatus,
    MicrosoftSyncCounts? Counts,
    IReadOnlyList<MicrosoftSyncProblem> Problems,
    IReadOnlyList<string> RolesNotInMicrosoft,
    DateTimeOffset? ClientSecretEndsUtc);

/// <summary>
/// The Microsoft directory sync: reads the app's assignments in Microsoft and brings the linked accounts of this app's database to them. The
/// library does not schedule it. The app calls <see cref="RunOnceAsync"/> from a hosted service registered with
/// <c>AddLeaderHostedService</c> (Ballast.Jobs), so that only the job holder runs it; <see cref="RunOnceAsync"/> checks
/// <see cref="IJobLeadership.IsHolder"/> itself as well, before it reads and again before it writes.
/// <para>
/// A run reads everything first. Then the plan (<see cref="MicrosoftSyncPlan"/>) is decided. If the plan would switch off every linked account
/// while some were on (a wrong tenant or an empty assignment list, for example), the run stops and writes nothing. Otherwise its changes are
/// written in one transaction, each person's change under its own savepoint. A failed read or write changes nothing. A profile hook
/// (<see cref="IMicrosoftStaffProfile{TUser,TKey}"/>) that throws undoes only that person's change, and that person is listed.
/// </para>
/// </summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRole">The app's Identity role.</typeparam>
/// <typeparam name="TRefresh">The app's refresh token entity.</typeparam>
/// <typeparam name="TContext">The app's database context.</typeparam>
public sealed class MicrosoftDirectorySync<TUser, TKey, TRole, TRefresh, TContext>
    where TUser : IdentityUser<TKey>, IAuthProfile, new()
    where TKey : IEquatable<TKey>
    where TRole : IdentityRole<TKey>, new()
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
    where TContext : DbContext
{
    private static readonly TimeSpan SecretWarning = TimeSpan.FromDays(30);

    private readonly MicrosoftSignInOptions _microsoft;
    private readonly IMicrosoftGraphClient _graph;
    private readonly IJobLeadership _leadership;
    private readonly TContext _db;
    private readonly UserManager<TUser> _users;
    private readonly RoleManager<TRole> _roles;
    private readonly MicrosoftStaffAccounts<TUser, TKey, TRole, TRefresh, TContext> _staff;
    private readonly IMicrosoftEmergencyAccounts<TKey> _emergency;
    private readonly TimeProvider _time;
    private readonly ILogger<MicrosoftDirectorySync<TUser, TKey, TRole, TRefresh, TContext>> _logger;

    /// <summary>Creates the sync.</summary>
    /// <param name="microsoft">The Microsoft settings: the tenant, the client secret and the roles.</param>
    /// <param name="graph">Reads the directory.</param>
    /// <param name="leadership">The job lock. Only the holder reads or writes.</param>
    /// <param name="db">The app's database context.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="roles">The role manager.</param>
    /// <param name="sessions">The session service, which ends the sessions of a changed or switched-off account.</param>
    /// <param name="audit">Where the audit rows go.</param>
    /// <param name="emergency">The app's emergency accounts. A run leaves them out: never switched off, never applied to.</param>
    /// <param name="profile">The app's own profile of a staff member. A hook that throws undoes only that person's change, and the person is listed.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">Logs the run. Messages name no token and no secret.</param>
    public MicrosoftDirectorySync(
        MicrosoftSignInOptions microsoft,
        IMicrosoftGraphClient graph,
        IJobLeadership leadership,
        TContext db,
        UserManager<TUser> users,
        RoleManager<TRole> roles,
        AuthSessionService<TUser, TKey, TRefresh, TContext> sessions,
        IAuthAudit audit,
        IMicrosoftEmergencyAccounts<TKey> emergency,
        IMicrosoftStaffProfile<TUser, TKey> profile,
        TimeProvider time,
        ILogger<MicrosoftDirectorySync<TUser, TKey, TRole, TRefresh, TContext>> logger)
    {
        _microsoft = microsoft;
        _graph = graph;
        _leadership = leadership;
        _db = db;
        _users = users;
        _roles = roles;
        _staff = new MicrosoftStaffAccounts<TUser, TKey, TRole, TRefresh, TContext>(users, roles, sessions, audit, profile, time);
        _emergency = emergency;
        _time = time;
        _logger = logger;
    }

    /// <summary>Runs the sync once. See the class remarks for what is read, decided and written.</summary>
    /// <param name="ct">Cancels the run. A cancelled run writes nothing it had not committed.</param>
    /// <returns>What the run came to.</returns>
    public async Task<MicrosoftSyncResult> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_microsoft.SyncEnabled)
        {
            return new MicrosoftSyncResult(MicrosoftSyncOutcome.Off, null, null, null, [], [], null);
        }

        if (!_leadership.IsHolder)
        {
            return new MicrosoftSyncResult(MicrosoftSyncOutcome.NotHolder, null, null, null, [], [], null);
        }

        MicrosoftDirectorySnapshot directory;
        try
        {
            directory = await _graph.ReadDirectoryAsync(ct);
        }
        catch (MicrosoftGraphException ex)
        {
            // The read failed before anything was written. The message names no secret and no token.
            _logger.LogError("The Microsoft directory read failed ({Failure}, HTTP {HttpStatus}). Nothing was applied. {Message}", ex.Failure, ex.HttpStatus, ex.Message);
            return new MicrosoftSyncResult(MicrosoftSyncOutcome.ReadFailed, ex.Failure, ex.HttpStatus, null, [], [], null);
        }

        var now = _time.GetUtcNow();
        var secretEnds = directory.ClientSecretEndsUtc;
        if (secretEnds is { } ends && ends - now < SecretWarning)
        {
            _logger.LogWarning("The Microsoft client secret ends {SecretEnds}. Create a new one and set its configuration before then.", ends);
        }

        var localRoles = await LocalRolesAsync(ct);
        var linked = await LinkedAccountsAsync(now, ct);
        var owners = await AddressOwnersAsync(directory, ct);
        var emergency = await EmergencyIdsAsync(ct);
        var plan = MicrosoftSyncPlan.Plan(directory, _microsoft, localRoles, linked, owners, emergency);

        foreach (var problem in plan.Problems)
        {
            _logger.LogWarning("Microsoft directory sync problem {Code} for {Subject}. It is listed and was not applied.", problem.Code, problem.Subject);
        }

        if (plan.LooksWrong)
        {
            _logger.LogError("The Microsoft directory sync would switch off every linked account, so nothing was applied. Check the tenant, the app roles and the assignments.");
            return new MicrosoftSyncResult(MicrosoftSyncOutcome.LooksWrong, null, null, plan.Counts, plan.Problems, plan.RolesNotInMicrosoft, secretEnds);
        }

        // Checked again: the lock can be lost while the directory is read.
        if (!_leadership.IsHolder)
        {
            return new MicrosoftSyncResult(MicrosoftSyncOutcome.NotHolder, null, null, plan.Counts, plan.Problems, plan.RolesNotInMicrosoft, secretEnds);
        }

        (List<MicrosoftSyncProblem> Listed, int Created) written;
        try
        {
            written = await WritePlanAsync(plan, emergency, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The Microsoft directory sync failed while it wrote its changes. They were rolled back.");
            return new MicrosoftSyncResult(MicrosoftSyncOutcome.WriteFailed, DirectorySyncFailure.Unexpected, null, plan.Counts, plan.Problems, plan.RolesNotInMicrosoft, secretEnds);
        }

        // The people whose change was rolled back are listed with the plan's own problems. The applied run's Created is the number that committed.
        return new MicrosoftSyncResult(
            MicrosoftSyncOutcome.Applied,
            null,
            null,
            plan.Counts with { Created = written.Created },
            [.. plan.Problems, .. written.Listed],
            plan.RolesNotInMicrosoft,
            secretEnds);
    }

    // The app's emergency accounts, as the text ids the plan and the write step use.
    private async Task<HashSet<string>> EmergencyIdsAsync(CancellationToken ct) =>
        (await _emergency.IdsAsync(ct)).Select(id => AuthKey.Text(id)).ToHashSet(StringComparer.Ordinal);

    // The savepoint each person's change runs under. A profile hook that throws rolls back to it, and the other people's changes stay.
    private const string PersonSavepoint = "MicrosoftSyncPerson";

    // The plan's changes, in one transaction. The execution strategy replays it whole after a transient fault, from a clean tracker. A profile hook
    // that throws undoes only its person's change, back to that person's savepoint, and the person is returned as listed; any other failure rolls
    // back the whole transaction, so a failed write changes nothing.
    private async Task<(List<MicrosoftSyncProblem> Listed, int Created)> WritePlanAsync(MicrosoftSyncPlanResult plan, HashSet<string> emergency, CancellationToken ct)
    {
        List<MicrosoftSyncProblem> listed = [];
        var created = 0;
        var attempt = 0;
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                _db.ChangeTracker.Clear();
                listed.Clear();
                created = 0;
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _staff.EnsureRolesAsync(plan.RolesToCreate);
                await _db.SaveChangesAsync(ct);

                foreach (var update in plan.Updates)
                {
                    // An emergency account is never applied to, even when the plan names it.
                    if (emergency.Contains(update.UserId))
                    {
                        continue;
                    }

                    var problem = await ChangePersonAsync(
                        transaction,
                        update.UserId,
                        update.Person.Address,
                        user => _staff.ApplyAsync(user, update.Person, update.Roles, ct),
                        ct);
                    if (problem is not null)
                    {
                        listed.Add(problem);
                    }
                }

                foreach (var creation in plan.Creations)
                {
                    var problem = await CreatePersonAsync(transaction, creation, ct);
                    if (problem is null)
                    {
                        created++;
                    }
                    else
                    {
                        listed.Add(problem);
                    }
                }

                foreach (var userId in plan.SwitchOffUserIds)
                {
                    if (emergency.Contains(userId))
                    {
                        continue;
                    }

                    var problem = await ChangePersonAsync(transaction, userId, userId, user => _staff.SwitchOffAsync(user, ct), ct);
                    if (problem is not null)
                    {
                        listed.Add(problem);
                    }
                }

                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                throw;
            }
        });

        return (listed, created);
    }

    // One person's change, inside the run's transaction and under the person's savepoint. A profile hook that throws is rolled back to the savepoint,
    // logged, and returned as that person's problem; the run goes on. Any other exception is left to the caller, which rolls back the whole run.
    private async Task<MicrosoftSyncProblem?> ChangePersonAsync(
        IDbContextTransaction transaction,
        string userId,
        string subject,
        Func<TUser, Task> change,
        CancellationToken ct)
    {
        await transaction.CreateSavepointAsync(PersonSavepoint, ct);
        try
        {
            var user = await _users.FindByIdAsync(userId);
            if (user is not null)
            {
                await change(user);
                await _db.SaveChangesAsync(ct);
            }

            return null;
        }
        catch (MicrosoftStaffProfileException ex)
        {
            await transaction.RollbackToSavepointAsync(PersonSavepoint, ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "The app's Microsoft staff profile hook failed for {Subject}. That person's change was rolled back; the other changes were kept.", subject);
            return new MicrosoftSyncProblem(MicrosoftSyncProblems.ProfileHookFailed, subject, ex.InnerException?.GetType().Name);
        }
    }

    // One person's creation, as a first Microsoft sign-in makes an account, under the person's own savepoint. Identity refusing the name or the address
    // (another account holds it) is listed as ADDRESS_TAKEN and nothing is linked by address. A profile hook that throws is rolled back and listed.
    // A database write that fails is rolled back and listed as CREATE_FAILED; any other exception is left to the caller, which rolls back the run.
    private async Task<MicrosoftSyncProblem?> CreatePersonAsync(IDbContextTransaction transaction, MicrosoftSyncCreation creation, CancellationToken ct)
    {
        var subject = creation.Person.Address;
        await transaction.CreateSavepointAsync(PersonSavepoint, ct);
        try
        {
            var user = await _staff.CreateAsync(creation.Person, creation.Roles, ct);
            if (user is null)
            {
                await transaction.RollbackToSavepointAsync(PersonSavepoint, ct);
                _db.ChangeTracker.Clear();
                return new MicrosoftSyncProblem(MicrosoftSyncProblems.AddressTaken, subject);
            }

            await _db.SaveChangesAsync(ct);
            return null;
        }
        catch (MicrosoftStaffProfileException ex)
        {
            await transaction.RollbackToSavepointAsync(PersonSavepoint, ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "The app's Microsoft staff profile hook failed while creating {Subject}. That person's creation was rolled back; the other changes were kept.", subject);
            return new MicrosoftSyncProblem(MicrosoftSyncProblems.ProfileHookFailed, subject, ex.InnerException?.GetType().Name);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackToSavepointAsync(PersonSavepoint, ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "The database refused the creation of the Microsoft account for {Subject}. That person's creation was rolled back; the other changes were kept.", subject);
            return new MicrosoftSyncProblem(MicrosoftSyncProblems.CreateFailed, subject, ex.GetType().Name);
        }
    }

    private async Task<List<MicrosoftLocalRole>> LocalRolesAsync(CancellationToken ct)
    {
        var roles = await _roles.Roles.AsNoTracking().ToListAsync(ct);
        var fromMicrosoft = (await _db.Set<IdentityRoleClaim<TKey>>().AsNoTracking()
            .Where(c => c.ClaimType == MicrosoftStaffMarks.SourceClaimType && c.ClaimValue == MicrosoftStaffMarks.SourceMicrosoft)
            .Select(c => c.RoleId)
            .ToListAsync(ct))
            .ToHashSet();

        return roles
            .Where(role => role.Name is not null)
            .Select(role => new MicrosoftLocalRole(role.Name!, fromMicrosoft.Contains(role.Id)))
            .ToList();
    }

    private async Task<List<MicrosoftLinkedAccount>> LinkedAccountsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var links = await _db.Set<IdentityUserLogin<TKey>>().AsNoTracking()
            .Where(login => login.LoginProvider == AuthProviders.Microsoft)
            .Select(login => new { login.UserId, login.ProviderKey })
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return [];
        }

        var ids = links.Select(link => link.UserId).ToList();
        var users = (await _users.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct))
            .ToDictionary(u => AuthKey.Text(u.Id), StringComparer.Ordinal);

        var roleRows = await (from userRole in _db.Set<IdentityUserRole<TKey>>().AsNoTracking()
                              join role in _roles.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                              where ids.Contains(userRole.UserId)
                              select new { userRole.UserId, role.Name })
            .ToListAsync(ct);
        var rolesByUser = roleRows
            .Where(row => row.Name is not null)
            .GroupBy(row => AuthKey.Text(row.UserId), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Name!).ToList(), StringComparer.Ordinal);

        List<MicrosoftLinkedAccount> accounts = [];
        foreach (var link in links)
        {
            var key = AuthKey.Text(link.UserId);
            if (!users.TryGetValue(key, out var user))
            {
                continue;
            }

            var roles = rolesByUser.TryGetValue(key, out var found) ? found : [];
            accounts.Add(new MicrosoftLinkedAccount(
                key,
                link.ProviderKey,
                user.Email,
                roles,
                MicrosoftStaffMarks.IsSwitchedOff(user.LockoutEnd, now)));
        }

        return accounts;
    }

    private async Task<Dictionary<string, string>> AddressOwnersAsync(MicrosoftDirectorySnapshot directory, CancellationToken ct)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var addresses = directory.Users.Values
            .Select(user => MicrosoftPersonReader.NormalizeAddress(user.UserPrincipalName))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (addresses.Count == 0)
        {
            return owners;
        }

        // Identity stores the normalised (upper-case) names, so the directory's addresses are compared in that form.
        var upper = addresses.Select(address => address.ToUpperInvariant()).ToList();
        var holders = await _users.Users.AsNoTracking()
            .Where(u => upper.Contains(u.NormalizedEmail!) || upper.Contains(u.NormalizedUserName!))
            .Select(u => new { u.Id, u.NormalizedEmail, u.NormalizedUserName })
            .ToListAsync(ct);

        foreach (var holder in holders)
        {
            foreach (var name in new[] { holder.NormalizedEmail, holder.NormalizedUserName })
            {
                var lower = name?.ToLowerInvariant();
                if (lower is not null && addresses.Contains(lower))
                {
                    owners.TryAdd(lower, AuthKey.Text(holder.Id));
                }
            }
        }

        return owners;
    }
}
