// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth;

/// <summary>The marks the library puts on a role it creates from Microsoft, and the reading of a switched-off account.</summary>
internal static class MicrosoftStaffMarks
{
    /// <summary>The role claim that marks a role created from Microsoft. Its permissions are set in the app, not in Microsoft.</summary>
    public const string SourceClaimType = "source";

    /// <summary>The value of <see cref="SourceClaimType"/> for a role created from Microsoft.</summary>
    public const string SourceMicrosoft = "microsoft";

    // Identity's own lockouts last minutes. A lockout this far ahead can only be the switch-off, which sets DateTimeOffset.MaxValue.
    private static readonly TimeSpan SwitchOffHorizon = TimeSpan.FromDays(365 * 100);

    /// <summary>True when the account's lockout is the switch-off, not one Identity set itself.</summary>
    /// <param name="lockoutEnd">The account's lockout end, or null.</param>
    /// <param name="now">The time now.</param>
    public static bool IsSwitchedOff(DateTimeOffset? lockoutEnd, DateTimeOffset now) =>
        lockoutEnd is { } end && end - now > SwitchOffHorizon;
}

/// <summary>
/// The account writes that the Microsoft sign-in and the directory sync share: create, apply, switch off. They open no transaction; the caller
/// wraps each sign-in or sync in one, so a failure part way leaves nothing behind. Every Identity result is checked, because a failed write
/// that is discarded would look like a success. A role or switch-off change ends the account's sessions: the security stamp changes (so the
/// access tokens it signed fail at the next request) and the refresh tokens are revoked.
/// </summary>
internal sealed class MicrosoftStaffAccounts<TUser, TKey, TRole, TRefresh, TContext>
    where TUser : IdentityUser<TKey>, IAuthProfile, new()
    where TKey : IEquatable<TKey>
    where TRole : IdentityRole<TKey>, new()
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
    where TContext : Microsoft.EntityFrameworkCore.DbContext
{
    private readonly UserManager<TUser> _users;
    private readonly RoleManager<TRole> _roles;
    private readonly AuthSessionService<TUser, TKey, TRefresh, TContext> _sessions;
    private readonly IAuthAudit _audit;
    private readonly IMicrosoftStaffProfile<TUser, TKey> _profile;
    private readonly TimeProvider _time;

    /// <summary>Creates the writer.</summary>
    public MicrosoftStaffAccounts(
        UserManager<TUser> users,
        RoleManager<TRole> roles,
        AuthSessionService<TUser, TKey, TRefresh, TContext> sessions,
        IAuthAudit audit,
        IMicrosoftStaffProfile<TUser, TKey> profile,
        TimeProvider time)
    {
        _users = users;
        _roles = roles;
        _sessions = sessions;
        _audit = audit;
        _profile = profile;
        _time = time;
    }

    /// <summary>The account that holds this Microsoft object id, or null when none does.</summary>
    public Task<TUser?> FindLinkedAsync(string objectId) => _users.FindByLoginAsync(AuthProviders.Microsoft, objectId);

    /// <summary>True when the account has a Microsoft login.</summary>
    public async Task<bool> HasMicrosoftLoginAsync(TUser user) =>
        (await _users.GetLoginsAsync(user)).Any(login => login.LoginProvider == AuthProviders.Microsoft);

    /// <summary>
    /// Returns the role name for each name, creating the missing ones with the <c>source = microsoft</c> claim and no permissions. Names match
    /// ignoring case, and an existing role keeps its own spelling.
    /// </summary>
    public async Task<IReadOnlyList<string>> EnsureRolesAsync(IEnumerable<string> names)
    {
        List<string> result = [];
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var role = await _roles.FindByNameAsync(name);
            if (role is null)
            {
                role = new TRole { Id = NewRoleKey(), Name = name };
                ThrowIfFailed(await _roles.CreateAsync(role), "Could not create a role");
                ThrowIfFailed(
                    await _roles.AddClaimAsync(role, new Claim(MicrosoftStaffMarks.SourceClaimType, MicrosoftStaffMarks.SourceMicrosoft)),
                    "Could not mark a role as created from Microsoft");
            }

            result.Add(role.Name ?? name);
        }

        return result;
    }

    /// <summary>
    /// True when one of the roles is the full-access role, or carries a permission claim of <paramref name="permissionClaimType"/>. Without
    /// one, a sign-in gets no session.
    /// </summary>
    public async Task<bool> HasPermissionAsync(IEnumerable<string> roleNames, string? fullAccessRole, string permissionClaimType)
    {
        foreach (var name in roleNames)
        {
            if (fullAccessRole is not null && string.Equals(name, fullAccessRole, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var role = await _roles.FindByNameAsync(name);
            if (role is null)
            {
                continue;
            }

            var claims = await _roles.GetClaimsAsync(role);
            if (claims.Any(claim => claim.Type == permissionClaimType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The new account for a person: the address as sign-in name and email, no password, the roles and the Microsoft login. The profile hook
    /// runs first, before the insert, and may set the account's key. Returns null when Identity refused it because another account has the name
    /// or the address (a lost race): the caller looks the account up again.
    /// </summary>
    public async Task<TUser?> CreateAsync(MicrosoftPerson person, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        var user = new TUser
        {
            UserName = person.Address,
            Email = person.Address,
            EmailConfirmed = true,
            FullName = $"{person.GivenName} {person.Surname}",
        };

        await RunHookAsync(person.Address, () => _profile.CreatingAsync(user, person, ct));

        var created = await _users.CreateAsync(user);
        if (!created.Succeeded)
        {
            if (created.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail"))
            {
                return null;
            }

            throw new InvalidOperationException($"Could not create the Microsoft account: {IdentityErrorText.Of(created.Errors)}");
        }

        ThrowIfFailed(await _users.AddToRolesAsync(user, roles), "Could not give the Microsoft account its roles");
        ThrowIfFailed(
            await _users.AddLoginAsync(user, new UserLoginInfo(AuthProviders.Microsoft, person.ObjectId, AuthProviders.Microsoft)),
            "Could not link the Microsoft account");

        Audit(user.Id, "MicrosoftAccountCreated");
        return user;
    }

    /// <summary>
    /// Brings a linked account to the person's current name, address and roles, and switches it on when it was switched off. The address is
    /// kept, not changed, when another account holds it. The profile hook runs after a name, address or role change. Returns true when anything
    /// changed.
    /// </summary>
    public async Task<bool> ApplyAsync(TUser user, MicrosoftPerson person, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        List<string> changes = [];

        var fullName = $"{person.GivenName} {person.Surname}";
        if (!string.Equals(user.FullName, fullName, StringComparison.Ordinal))
        {
            user.FullName = fullName;
            changes.Add("name");
        }

        var addressChanged = !string.Equals(user.Email, person.Address, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(user.UserName, person.Address, StringComparison.OrdinalIgnoreCase);
        if (addressChanged && !await HeldByOtherAsync(user, person.Address))
        {
            user.Email = person.Address;
            user.UserName = person.Address;
            changes.Add("address");
        }

        if (changes.Count > 0)
        {
            ThrowIfFailed(await _users.UpdateAsync(user), "Could not save the Microsoft account");
        }

        // Exactly the roles Microsoft gives for this console: any other role of the account is removed.
        var current = await _users.GetRolesAsync(user);
        var toRemove = current.Where(role => !roles.Contains(role, StringComparer.OrdinalIgnoreCase)).ToList();
        var toAdd = roles.Where(role => !current.Contains(role, StringComparer.OrdinalIgnoreCase)).ToList();
        if (toRemove.Count > 0)
        {
            ThrowIfFailed(await _users.RemoveFromRolesAsync(user, toRemove), "Could not remove the roles");
        }

        if (toAdd.Count > 0)
        {
            ThrowIfFailed(await _users.AddToRolesAsync(user, toAdd), "Could not add the roles");
        }

        var roleChanged = toRemove.Count > 0 || toAdd.Count > 0;
        if (roleChanged)
        {
            changes.Add("roles");
        }

        // The profile follows a name, address or role change. A switch-on alone does not reach it.
        var profileChanged = changes.Count > 0;
        if (profileChanged)
        {
            await RunHookAsync(person.Address, () => _profile.AppliedAsync(user, person, ct));
        }

        var switchingOn = MicrosoftStaffMarks.IsSwitchedOff(user.LockoutEnd, now);
        if (switchingOn)
        {
            ThrowIfFailed(await _users.SetLockoutEndDateAsync(user, null), "Could not switch the account on");
            changes.Add("switched on");
        }

        if (roleChanged || switchingOn)
        {
            await EndSessionsAsync(user);
        }

        if (changes.Count > 0)
        {
            Audit(user.Id, "MicrosoftAccountUpdated");
        }

        return changes.Count > 0;
    }

    /// <summary>
    /// Switches an account off: no roles, locked out until further notice, its sessions ended. The sign-in refuses a locked account, and the
    /// sync turns it back on when Microsoft lets the person in again. The profile hook runs after the switch-off.
    /// </summary>
    public async Task SwitchOffAsync(TUser user, CancellationToken ct = default)
    {
        var roles = await _users.GetRolesAsync(user);
        if (roles.Count > 0)
        {
            ThrowIfFailed(await _users.RemoveFromRolesAsync(user, roles), "Could not remove the roles of a switched-off account");
        }

        ThrowIfFailed(await _users.SetLockoutEnabledAsync(user, true), "Could not lock the account");
        ThrowIfFailed(await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue), "Could not lock the account");

        await EndSessionsAsync(user);
        await RunHookAsync(user.Email ?? AuthKey.Text(user.Id), () => _profile.SwitchedOffAsync(user, ct));
        Audit(user.Id, "MicrosoftAccountSwitchedOff");
    }

    // The stamp changes first, so the access tokens the account holds fail at their next request; then the refresh tokens are revoked.
    private async Task EndSessionsAsync(TUser user)
    {
        ThrowIfFailed(await _users.UpdateSecurityStampAsync(user), "Could not change the security stamp");
        await _sessions.RevokeAllAsync(user.Id);
    }

    private async Task<bool> HeldByOtherAsync(TUser user, string address)
    {
        var byName = await _users.FindByNameAsync(address);
        var byEmail = await _users.FindByEmailAsync(address);
        return (byName is not null && !EqualityComparer<TKey>.Default.Equals(byName.Id, user.Id))
            || (byEmail is not null && !EqualityComparer<TKey>.Default.Equals(byEmail.Id, user.Id));
    }

    private void Audit(TKey userId, string action)
    {
        var id = AuthKey.Text(userId);
        _audit.Add(new AuthAuditEntry(id, action, "User", id, _time.GetUtcNow().UtcDateTime));
    }

    // Identity does not give a role a key of its own for a generic key type, so a GUID is made here. An int key is left at its default, and the database generates it.
    private static TKey NewRoleKey()
    {
        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)Guid.NewGuid().ToString();
        }

        return typeof(TKey) == typeof(Guid) ? (TKey)(object)Guid.NewGuid() : default!;
    }

    // A hook's own failure is wrapped, so the caller sees that the write it belonged to failed. Wrapping also keeps an Identity or database
    // error from the hook from being taken for a lost race on the create. A cancellation passes through unchanged.
    private static async Task RunHookAsync(string subject, Func<Task> hook)
    {
        try
        {
            await hook();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MicrosoftStaffProfileException(subject, ex);
        }
    }

    private static void ThrowIfFailed(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{what}: {IdentityErrorText.Of(result.Errors)}");
        }
    }
}
