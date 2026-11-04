// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Ballast.Errors;
using Ballast.SqlServer;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Ballast.Auth;

/// <summary>The result of the Microsoft handshake: the account to sign in, or the one refusal the sign-in page shows.</summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <param name="UserId">The account, when accepted; null when refused.</param>
/// <param name="RefusalCode">The refusal, one of <see cref="MicrosoftRefusal"/>; null when accepted.</param>
public sealed record MicrosoftSignInOutcome<TKey>(TKey? UserId, string? RefusalCode)
{
    /// <summary>True when the person may get a one-time code.</summary>
    public bool IsAccepted => RefusalCode is null;

    /// <summary>The outcome for an account that may sign in.</summary>
    public static MicrosoftSignInOutcome<TKey> Accepted(TKey userId) => new(userId, null);

    /// <summary>The outcome for a refusal.</summary>
    public static MicrosoftSignInOutcome<TKey> Refused(string refusal) => new(default, refusal);
}

/// <summary>
/// Microsoft staff sign-in. The browser's handshake (the OpenID Connect callback, see <see cref="MicrosoftSignInRegistration"/>) produces a
/// principal; <see cref="CompleteAsync(System.Security.Claims.ClaimsPrincipal,System.Threading.CancellationToken)"/> decides it and writes the account; <see cref="IssueCodeAsync"/> makes a one-time code the sign-in page
/// carries; <see cref="ExchangeAsync"/> turns that code into a session. The code is stored, hashed, in the app's Identity token table, so a
/// code issued on one copy of an app can be exchanged on another during a deploy.
/// <para>
/// An account is matched by the person's Microsoft object id, never by address. A password account that holds the person's address is
/// refused, not linked (<see cref="MicrosoftRefusal.AddressTaken"/>), because the address of a password account was never proven to belong
/// to the person.
/// </para>
/// </summary>
/// <typeparam name="TUser">The app's Identity user, with a parameterless constructor.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRole">The app's Identity role.</typeparam>
/// <typeparam name="TRefresh">The app's refresh token entity.</typeparam>
/// <typeparam name="TContext">The app's database context, derived from an Identity context so it has the token table.</typeparam>
public sealed class MicrosoftSignInService<TUser, TKey, TRole, TRefresh, TContext>
    where TUser : IdentityUser<TKey>, IAuthProfile, new()
    where TKey : IEquatable<TKey>
    where TRole : IdentityRole<TKey>, new()
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
    where TContext : DbContext
{
    /// <summary>The login provider of the one-time codes in the Identity token table.</summary>
    public const string CodeProvider = "MicrosoftSignInCode";

    /// <summary>How long a one-time code is valid. The sign-in page exchanges it at once, so a minute is ample.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

    private readonly AuthOptions _auth;
    private readonly MicrosoftSignInOptions _microsoft;
    private readonly TContext _db;
    private readonly UserManager<TUser> _users;
    private readonly AuthSessionService<TUser, TKey, TRefresh, TContext> _sessions;
    private readonly IAuthAudit _audit;
    private readonly IMicrosoftEmergencyAccounts<TKey> _emergency;
    private readonly TimeProvider _time;
    private readonly ILogger<MicrosoftSignInService<TUser, TKey, TRole, TRefresh, TContext>> _logger;
    private readonly MicrosoftStaffAccounts<TUser, TKey, TRole, TRefresh, TContext> _staff;

    /// <summary>Creates the service.</summary>
    /// <param name="auth">The sign-in options, whose <see cref="AuthOptions.Failures"/> give the one-time-code refusal.</param>
    /// <param name="microsoft">The Microsoft settings: the tenant, the company domains and the roles.</param>
    /// <param name="db">The app's database context.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="roles">The role manager.</param>
    /// <param name="sessions">The session service, which issues the session once the account is proven.</param>
    /// <param name="audit">Where the audit rows go.</param>
    /// <param name="emergency">The app's emergency accounts. A sign-in never links one to a Microsoft login, and never changes one.</param>
    /// <param name="profile">The app's own profile of a staff member. Its hooks run in the sign-in's transaction; a hook that throws rolls the sign-in back.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">Where each refusal is logged, with its code and the Microsoft object id.</param>
    public MicrosoftSignInService(
        AuthOptions auth,
        MicrosoftSignInOptions microsoft,
        TContext db,
        UserManager<TUser> users,
        RoleManager<TRole> roles,
        AuthSessionService<TUser, TKey, TRefresh, TContext> sessions,
        IAuthAudit audit,
        IMicrosoftEmergencyAccounts<TKey> emergency,
        IMicrosoftStaffProfile<TUser, TKey> profile,
        TimeProvider time,
        ILogger<MicrosoftSignInService<TUser, TKey, TRole, TRefresh, TContext>> logger)
    {
        _auth = auth;
        _microsoft = microsoft;
        _db = db;
        _users = users;
        _sessions = sessions;
        _audit = audit;
        _emergency = emergency;
        _time = time;
        _logger = logger;
        _staff = new MicrosoftStaffAccounts<TUser, TKey, TRole, TRefresh, TContext>(users, roles, sessions, audit, profile, time);
    }

    /// <summary>
    /// Decides a Microsoft sign-in from the handshake's principal, and writes the account. A first sign-in creates it; a later one brings it up
    /// to date. Returns the account to sign in, or the refusal the sign-in page shows.
    /// </summary>
    /// <param name="principal">The principal the handshake produced.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    private async Task<MicrosoftSignInOutcome<TKey>> DecideAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!_microsoft.IsEnabled)
        {
            return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.NotConfigured);
        }

        var (person, refusal) = MicrosoftPersonReader.Read(principal, _microsoft);
        if (person is null)
        {
            return MicrosoftSignInOutcome<TKey>.Refused(refusal ?? MicrosoftRefusal.SignInFailed);
        }

        if (!_microsoft.IsCompanyAddress(person.Address))
        {
            return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.NotCompanyAddress);
        }

        // This console's roles only. A rider role or a value for the other console is not a staff role here.
        var roles = MicrosoftRoleRules.ForConsole(person.RoleValues, _microsoft).Roles;
        if (roles.Count == 0)
        {
            return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.NoRole);
        }

        if (_microsoft.RoleMode == MicrosoftRoleMode.One && roles.Count > 1)
        {
            return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.MultipleRoles);
        }

        var linked = await _staff.FindLinkedAsync(person.ObjectId);
        var emergency = await _emergency.IdsAsync(ct);
        if (linked is not null && emergency.Contains(linked.Id))
        {
            // An emergency account is never changed by a sign-in: its Microsoft login is refused, and nothing is written.
            return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.EmergencyAccount);
        }

        if (linked is null && await HoldsAddressAsync(person.Address) is { } holder)
        {
            // The password account is never linked: the refusal is audited on it, and nothing else changes.
            var id = AuthKey.Text(holder.Id);
            _audit.Add(new AuthAuditEntry(id, "MicrosoftSignInRefusedExistingAccount", "User", id, Now()));
            await _db.SaveChangesAsync(ct);
            return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.AddressTaken);
        }

        var account = await WriteAccountAsync(person, roles, linked, ct);
        if (account is null)
        {
            // A concurrent first sign-in of this person created the account and its login after the look-up above. That is this person's
            // account, so it is used. If no login exists, another account holds the address.
            var raced = await _staff.FindLinkedAsync(person.ObjectId);
            if (raced is null)
            {
                return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.AddressTaken);
            }

            if (emergency.Contains(raced.Id))
            {
                return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.EmergencyAccount);
            }

            account = await WriteAccountAsync(person, roles, raced, ct)
                ?? throw new InvalidOperationException("The Microsoft account written by a concurrent sign-in could not be used.");
        }

        // Microsoft let the person in, but no role here has a permission yet. The account and its roles are saved, so the owner can set the
        // permissions. Until then the person gets no session.
        if (!await _staff.HasPermissionAsync(roles, _microsoft.FullAccessRole, _microsoft.PermissionClaimType))
        {
            return MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.RoleNoPermissions);
        }

        return MicrosoftSignInOutcome<TKey>.Accepted(account.Id);
    }

    /// <summary>
    /// Decides a Microsoft sign-in from the handshake's principal, and writes the account. A first sign-in creates it; a later one brings it up
    /// to date. Returns the account to sign in, or the refusal the sign-in page shows. Every refusal is logged at Warning with its code and the
    /// Microsoft object id, never the address, a token or a name.
    /// </summary>
    /// <param name="principal">The principal the handshake produced.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    public async Task<MicrosoftSignInOutcome<TKey>> CompleteAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var outcome = await DecideAsync(principal, ct);
        if (!outcome.IsAccepted)
        {
            _logger.LogWarning(
                "Microsoft sign-in refused ({Refusal}, Microsoft object id {ObjectId}).",
                outcome.RefusalCode,
                principal.FindFirst("oid")?.Value ?? "none");
        }

        return outcome;
    }

    /// <summary>
    /// Completes the handshake of a request: reads the handshake cookie, decides the sign-in as the principal overload does, removes the cookie in
    /// every case, and returns the sign-in page's address. That address carries a one-time code when the sign-in is accepted, and the refusal when
    /// it is not. The library's complete route redirects to it, and so may an app's own callback route.
    /// </summary>
    /// <param name="http">The request whose handshake cookie carries Microsoft's result.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The path and query of the sign-in page to redirect to.</returns>
    public async Task<string> CompleteAsync(HttpContext http, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!_microsoft.IsEnabled)
        {
            return MicrosoftSignInRedirects.Error(_microsoft.LoginPagePath, MicrosoftRefusal.NotConfigured);
        }

        // The handshake's result is read and removed in every case: a refusal, a success and a throw all leave the cookie gone.
        MicrosoftSignInOutcome<TKey> outcome;
        try
        {
            var handshake = await http.AuthenticateAsync(_microsoft.CookieScheme);
            outcome = handshake.Succeeded && handshake.Principal is not null
                ? await CompleteAsync(handshake.Principal, ct)
                : MicrosoftSignInOutcome<TKey>.Refused(MicrosoftRefusal.SignInFailed);
        }
        finally
        {
            await http.SignOutAsync(_microsoft.CookieScheme);
        }

        if (!outcome.IsAccepted)
        {
            return MicrosoftSignInRedirects.Error(_microsoft.LoginPagePath, outcome.RefusalCode!);
        }

        var code = await IssueCodeAsync(outcome.UserId!, ct);
        return MicrosoftSignInRedirects.Code(_microsoft.LoginPagePath, code);
    }

    /// <summary>
    /// Issues the one-time code the sign-in page exchanges for the session. The code is random and shown once; the database keeps its SHA-256
    /// and the expiry. One live code is kept per account: an earlier one is removed first.
    /// </summary>
    /// <param name="userId">The account the code is for.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The code, in URL-safe base64.</returns>
    public async Task<string> IssueCodeAsync(TKey userId, CancellationToken ct = default)
    {
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expires = _time.GetUtcNow().Add(CodeLifetime);
        var tokens = _db.Set<IdentityUserToken<TKey>>();

        // The codes table is small and each row lives a minute, so the account's own rows are picked in memory.
        var older = (await tokens.Where(t => t.LoginProvider == CodeProvider).ToListAsync(ct))
            .Where(t => EqualityComparer<TKey>.Default.Equals(t.UserId, userId))
            .ToList();
        tokens.RemoveRange(older);

        tokens.Add(new IdentityUserToken<TKey>
        {
            UserId = userId,
            LoginProvider = CodeProvider,
            Name = HashCode(code),
            Value = expires.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        });
        await _db.SaveChangesAsync(ct);
        return code;
    }

    /// <summary>
    /// Exchanges a one-time code for the session. The code works once: its row is claimed by a conditional delete, so two exchanges of the same
    /// code cannot both succeed. The account is checked again at the exchange, so a switch-off or a lock made after the code was issued is
    /// honoured. Every refusal is the same answer.
    /// </summary>
    /// <param name="code">The code from the sign-in redirect.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The session.</returns>
    /// <exception cref="AppException">The code is unknown, used, expired, or its account is no longer a signed-in Microsoft account.</exception>
    public async Task<IssuedSession> ExchangeAsync(string code, CancellationToken ct = default)
    {
        var hash = HashCode(code);
        var tokens = _db.Set<IdentityUserToken<TKey>>();
        var row = await tokens.AsNoTracking().FirstOrDefaultAsync(t => t.LoginProvider == CodeProvider && t.Name == hash, ct)
            ?? throw CodeInvalid();

        // Claimed by the delete itself: a second exchange of the same code finds no row to delete.
        var claimed = await tokens.Where(t => t.LoginProvider == CodeProvider && t.Name == hash).ExecuteDeleteAsync(ct);
        if (claimed == 0)
        {
            throw CodeInvalid();
        }

        var now = _time.GetUtcNow();
        if (!DateTimeOffset.TryParse(row.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires) || expires <= now)
        {
            throw CodeInvalid();
        }

        // Recheck: the account must still be a Microsoft account, not switched off by the sync and not locked since the code was issued.
        var user = await _users.FindByIdAsync(AuthKey.Text(row.UserId));
        if (user is null
            || !await _staff.HasMicrosoftLoginAsync(user)
            || MicrosoftStaffMarks.IsSwitchedOff(user.LockoutEnd, now)
            || await _users.IsLockedOutAsync(user))
        {
            throw CodeInvalid();
        }

        return await _sessions.IssueAsync(user, rememberMe: false, "MicrosoftLogin", ct);
    }

    /// <summary>
    /// Creates the account, or brings the linked one up to date, in one transaction with its roles and its Microsoft login. Returns null when the
    /// create lost a race for the address or the name (the attempt is rolled back, and nothing of it is kept).
    /// </summary>
    private async Task<TUser?> WriteAccountAsync(MicrosoftPerson person, IReadOnlyList<string> roles, TUser? linked, CancellationToken ct)
    {
        TUser? written = null;

        // Inside the execution strategy, so a transient fault replays the whole write from a clean tracker.
        var attempt = 0;
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                _db.ChangeTracker.Clear();
            }

            written = null;
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var names = await _staff.EnsureRolesAsync(roles);
                TUser? user;
                if (linked is null)
                {
                    user = await _staff.CreateAsync(person, names, ct);
                    if (user is null)
                    {
                        await AbandonLostRaceAsync(transaction, ct);
                        return;
                    }
                }
                else
                {
                    user = await _users.FindByIdAsync(AuthKey.Text(linked.Id)) ?? linked;
                    await _staff.ApplyAsync(user, person, names, ct);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                written = user;
            }
            catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
            {
                await AbandonLostRaceAsync(transaction, ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });

        return written;
    }

    private async Task AbandonLostRaceAsync(IDbContextTransaction transaction, CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);

        // The rolled-back rows are still tracked; left there, the next save in this request would write them again.
        _db.ChangeTracker.Clear();
    }

    // A password account that holds this address, by sign-in name or by email.
    private async Task<TUser?> HoldsAddressAsync(string address) =>
        await _users.FindByEmailAsync(address) ?? await _users.FindByNameAsync(address);

    private static string HashCode(string code) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private AppException CodeInvalid() => _auth.Failures.MicrosoftCodeInvalid.ToException();

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
