// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth;

/// <summary>
/// The changes a signed-in person makes to their own sign-in: change the password, or set the first one on an account that has none.
/// A password change ends every other session: the security stamp changes, so the access tokens already out stop at the next request,
/// and every live refresh token is revoked.
/// </summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRefresh">The app's refresh token entity.</typeparam>
/// <typeparam name="TContext">The app's database context.</typeparam>
public sealed class AuthAccountService<TUser, TKey, TRefresh, TContext>
    where TUser : IdentityUser<TKey>, IAuthProfile
    where TKey : IEquatable<TKey>
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
    where TContext : DbContext
{
    private readonly AuthOptions _options;
    private readonly TContext _db;
    private readonly UserManager<TUser> _users;
    private readonly AuthSessionService<TUser, TKey, TRefresh, TContext> _sessions;
    private readonly IAuthAudit _audit;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    /// <param name="options">The validated options.</param>
    /// <param name="db">The app's database context.</param>
    /// <param name="users">The user manager, which checks and sets the password.</param>
    /// <param name="sessions">The session service, which ends the other sessions.</param>
    /// <param name="audit">Where the audit rows go.</param>
    /// <param name="time">The clock.</param>
    public AuthAccountService(AuthOptions options, TContext db, UserManager<TUser> users, AuthSessionService<TUser, TKey, TRefresh, TContext> sessions, IAuthAudit audit, TimeProvider time)
    {
        _options = options;
        _db = db;
        _users = users;
        _sessions = sessions;
        _audit = audit;
        _time = time;
    }

    /// <summary>
    /// Changes a password: the current one is checked, the new one meets the password rules, the security stamp changes, the change is
    /// audited, and every live refresh token of the user is revoked.
    /// </summary>
    /// <param name="userId">The signed-in user, as text.</param>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="AppException">
    /// The user does not exist (<see cref="AuthFailures.UserNotFound"/>), a Microsoft account (<see cref="AuthFailures.MicrosoftAccount"/>),
    /// an account with no password (<see cref="AuthFailures.NoPasswordSet"/>), or Identity refused the change (the
    /// <see cref="AuthFailures.Unclassified"/> code, with Identity's words as the message).
    /// </exception>
    public async Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var user = await _users.FindByIdAsync(userId)
            ?? throw _options.Failures.UserNotFound.ToException();

        if (await IsMicrosoftAccountAsync(user))
        {
            throw _options.Failures.MicrosoftAccount.ToException();
        }

        // Identity's own answer for an account with no password is a generic failure; say what it is.
        if (!await _users.HasPasswordAsync(user))
        {
            throw _options.Failures.NoPasswordSet.ToException();
        }

        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            throw Unclassified(result.Errors);
        }

        // The stamp changes in the same save, so the access tokens already out stop at their next request.
        var stamp = await _users.UpdateSecurityStampAsync(user);
        if (!stamp.Succeeded)
        {
            throw Unclassified(stamp.Errors);
        }

        var id = AuthKey.Text(user.Id);
        _audit.Add(new AuthAuditEntry(id, "PasswordChanged", "User", id, _time.GetUtcNow().UtcDateTime));
        await _db.SaveChangesAsync(ct);

        await _sessions.RevokeAllAsync(user.Id, ct);
    }

    /// <summary>
    /// Sets the first password on an account that has none, such as one created by Google sign-in. The password meets the rules, and the
    /// change is audited. Other sessions are not ended: the account had no password to steal.
    /// </summary>
    /// <param name="userId">The signed-in user, as text.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="AppException">
    /// The user does not exist (<see cref="AuthFailures.UserNotFound"/>), a Microsoft account (<see cref="AuthFailures.MicrosoftAccount"/>),
    /// the account already has a password (<see cref="AuthFailures.PasswordAlreadySet"/>), or Identity refused the password (the
    /// <see cref="AuthFailures.Unclassified"/> code).
    /// </exception>
    public async Task SetPasswordAsync(string userId, string newPassword, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var user = await _users.FindByIdAsync(userId)
            ?? throw _options.Failures.UserNotFound.ToException();

        if (await IsMicrosoftAccountAsync(user))
        {
            throw _options.Failures.MicrosoftAccount.ToException();
        }

        if (await _users.HasPasswordAsync(user))
        {
            throw _options.Failures.PasswordAlreadySet.ToException();
        }

        var result = await _users.AddPasswordAsync(user, newPassword);
        if (!result.Succeeded)
        {
            throw Unclassified(result.Errors);
        }

        var id = AuthKey.Text(user.Id);
        _audit.Add(new AuthAuditEntry(id, "PasswordSet", "User", id, _time.GetUtcNow().UtcDateTime));
        await _db.SaveChangesAsync(ct);
    }

    private async Task<bool> IsMicrosoftAccountAsync(TUser user) =>
        (await _users.GetLoginsAsync(user)).Any(l => l.LoginProvider == AuthProviders.Microsoft);

    private AppException Unclassified(IEnumerable<IdentityError> errors) =>
        new(_options.Failures.Unclassified.Code, IdentityErrorText.Of(errors), _options.Failures.Unclassified.Status);
}
