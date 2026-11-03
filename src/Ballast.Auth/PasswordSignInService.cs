// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth;

/// <summary>
/// Proves a password sign-in. Every refusal is the same answer, and every refusal costs the same time as a wrong password: an
/// unknown address, a Microsoft account, a user outside the sign-in mode, a locked account and a password-less account all spend a
/// password hash before they are refused. A failed sign-in is audited and saved, and a locked account is not asked for its password.
/// </summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TContext">The app's database context, which holds the audit row.</typeparam>
public sealed class PasswordSignInService<TUser, TKey, TContext>
    where TUser : IdentityUser<TKey>, IAuthProfile, new()
    where TKey : IEquatable<TKey>
    where TContext : DbContext
{
    private readonly AuthOptions _options;
    private readonly UserManager<TUser> _users;
    private readonly SignInManager<TUser> _signIn;
    private readonly TContext _db;
    private readonly IAuthAudit _audit;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    /// <param name="options">The validated options.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="signIn">The sign-in manager, which counts failures and enforces the lockout.</param>
    /// <param name="db">The app's database context.</param>
    /// <param name="audit">Where the audit rows go.</param>
    /// <param name="time">The clock for the audit rows.</param>
    public PasswordSignInService(AuthOptions options, UserManager<TUser> users, SignInManager<TUser> signIn, TContext db, IAuthAudit audit, TimeProvider time)
    {
        _options = options;
        _users = users;
        _signIn = signIn;
        _db = db;
        _audit = audit;
        _time = time;
    }

    /// <summary>
    /// Checks an address and a password, and returns the user when both are right. The caller then issues the session.
    /// </summary>
    /// <param name="email">The address the person typed.</param>
    /// <param name="password">The password the person typed.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The signed-in user.</returns>
    /// <exception cref="Ballast.Errors.AppException">
    /// The sign-in mode is off (<see cref="AuthFailures.PasswordSignInOff"/>), or the credentials are refused
    /// (<see cref="AuthFailures.InvalidCredentials"/>). Both are the library's refusal codes.
    /// </exception>
    public async Task<TUser> CheckPasswordAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(password);

        if (_options.PasswordSignIn == AuthPasswordSignInMode.Off)
        {
            throw _options.Failures.PasswordSignInOff.ToException();
        }

        var user = await _users.FindByEmailAsync(email);
        if (user is null)
        {
            AccountEnumerationGuard.SpendPasswordHashingTime(_users, password);
            throw _options.Failures.InvalidCredentials.ToException();
        }

        // A Microsoft account has no password: its staff sign in with Microsoft. It answers as a wrong password does, with the same
        // hashing time, the same audit row and the same answer. Its lockout counter is not touched, so a guessed password cannot lock
        // a staff account.
        if (await IsMicrosoftAccountAsync(user))
        {
            AccountEnumerationGuard.SpendPasswordHashingTime(_users, password);
            await RecordFailureAsync(user, ct);
            throw _options.Failures.InvalidCredentials.ToException();
        }

        // Only the privileged role may sign in with a password in OwnerOnly mode. The check runs after the hashing time is spent, so the
        // answer for another role costs what a wrong password costs.
        if (_options.PasswordSignIn == AuthPasswordSignInMode.OwnerOnly && !await _users.IsInRoleAsync(user, _options.PrivilegedRoleName!))
        {
            AccountEnumerationGuard.SpendPasswordHashingTime(_users, password);
            await RecordFailureAsync(user, ct);
            throw _options.Failures.InvalidCredentials.ToException();
        }

        // The sign-in manager counts the failure and enforces the lockout. It never asks the hasher for a locked account.
        var result = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            // A locked-out account and a password-less (Google-only) one answer without hashing, which would make them faster than a
            // wrong password. Spend the same time as a real check.
            if (result.IsLockedOut || user.PasswordHash is null)
            {
                AccountEnumerationGuard.SpendPasswordHashingTime(_users, password);
            }

            await RecordFailureAsync(user, ct);
            throw _options.Failures.InvalidCredentials.ToException();
        }

        return user;
    }

    private async Task<bool> IsMicrosoftAccountAsync(TUser user) =>
        (await _users.GetLoginsAsync(user)).Any(l => l.LoginProvider == AuthProviders.Microsoft);

    private async Task RecordFailureAsync(TUser user, CancellationToken ct)
    {
        var id = AuthKey.Text(user.Id);
        _audit.Add(new AuthAuditEntry(id, "LoginFailed", "User", id, _time.GetUtcNow().UtcDateTime));
        await _db.SaveChangesAsync(ct);
    }
}
