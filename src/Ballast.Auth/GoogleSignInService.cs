// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;
using Ballast.SqlServer;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ballast.Auth;

/// <summary>
/// Google sign-in: the first sign-in creates a passwordless account, a later one signs into the same account, and an account is linked to
/// or unlinked from a Google login on request. The account is matched by Google's stable subject, never by address. A Google address that
/// belongs to a password account is refused, not linked, because the password account's address was never proven.
/// </summary>
/// <typeparam name="TUser">The app's Identity user, with a parameterless constructor.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRefresh">The app's refresh token entity.</typeparam>
/// <typeparam name="TContext">The app's database context.</typeparam>
public sealed class GoogleSignInService<TUser, TKey, TRefresh, TContext>
    where TUser : IdentityUser<TKey>, IAuthProfile, new()
    where TKey : IEquatable<TKey>
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
    where TContext : DbContext
{
    private readonly AuthOptions _options;
    private readonly AuthGoogleOptions _google;
    private readonly TContext _db;
    private readonly UserManager<TUser> _users;
    private readonly AuthSessionService<TUser, TKey, TRefresh, TContext> _sessions;
    private readonly IGoogleIdTokenValidator _validator;
    private readonly IAuthAudit _audit;
    private readonly IAuthNewAccount<TUser> _newAccount;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    /// <param name="options">The sign-in options, whose <see cref="AuthOptions.Failures"/> give the refusals.</param>
    /// <param name="google">The Google settings: the role a new account gets.</param>
    /// <param name="db">The app's database context. The account, its login, its role and the app's rows are written here.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="sessions">The session service, which issues the session once the account is proven.</param>
    /// <param name="validator">Checks the ID token with Google.</param>
    /// <param name="audit">Where the audit rows go.</param>
    /// <param name="newAccount">The app's rows for a new account.</param>
    /// <param name="time">The clock.</param>
    public GoogleSignInService(
        AuthOptions options,
        AuthGoogleOptions google,
        TContext db,
        UserManager<TUser> users,
        AuthSessionService<TUser, TKey, TRefresh, TContext> sessions,
        IGoogleIdTokenValidator validator,
        IAuthAudit audit,
        IAuthNewAccount<TUser> newAccount,
        TimeProvider time)
    {
        _options = options;
        _google = google;
        _db = db;
        _users = users;
        _sessions = sessions;
        _validator = validator;
        _audit = audit;
        _newAccount = newAccount;
        _time = time;
    }

    /// <summary>
    /// Signs in with a Google ID token. A token whose Google login is known signs into that account. A first sign-in with a verified address
    /// creates the account (no password, the configured role, the app's rows) and signs into it. A first sign-in whose address belongs to a
    /// password account is refused, never linked.
    /// </summary>
    /// <param name="idToken">The ID token from the app's Google sign-in.</param>
    /// <param name="rememberMe">Whether to use the longer refresh lifetime.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The session.</returns>
    /// <exception cref="AppException">
    /// The token is refused (<see cref="AuthFailures.GoogleTokenInvalid"/>), the address is not verified by Google
    /// (<see cref="AuthFailures.GoogleEmailUnverified"/>), the address belongs to a password account
    /// (<see cref="AuthFailures.AccountExistsUsePassword"/>), the account is locked (<see cref="AuthFailures.InvalidCredentials"/>), or Google
    /// is not configured or cannot be reached (<see cref="AuthFailures.GoogleSignInNotConfigured"/>, <see cref="AuthFailures.GoogleUnreachable"/>).
    /// </exception>
    public async Task<IssuedSession> SignInAsync(string idToken, bool rememberMe, CancellationToken ct = default)
    {
        var identity = await _validator.ValidateAsync(idToken, ct) ?? throw _options.Failures.GoogleTokenInvalid.ToException();

        var linked = await _users.FindByLoginAsync(AuthProviders.Google, identity.Subject);
        if (linked is not null)
        {
            return await SignInLinkedAsync(linked, rememberMe, ct);
        }

        if (!identity.EmailVerified)
        {
            throw _options.Failures.GoogleEmailUnverified.ToException();
        }

        var sameEmail = await _users.FindByEmailAsync(identity.Email);
        if (sameEmail is not null)
        {
            // A concurrent first sign-in with this same Google identity may have created the account since the lookup above. That is this
            // identity, not a password account, so sign in.
            linked = await _users.FindByLoginAsync(AuthProviders.Google, identity.Subject);
            if (linked is not null)
            {
                return await SignInLinkedAsync(linked, rememberMe, ct);
            }

            // Never linked automatically: the password account's address was typed by someone who may not own it, and linking would hand the
            // account to whoever controls the Google account. Refusing hands the Google owner nothing.
            var id = AuthKey.Text(sameEmail.Id);
            _audit.Add(new AuthAuditEntry(id, "GoogleSignInRefusedExistingAccount", "User", id, Now()));
            await _db.SaveChangesAsync(ct);
            throw _options.Failures.AccountExistsUsePassword.ToException();
        }

        var created = await TryCreateAsync(identity, ct);
        if (created is null)
        {
            // Lost a race with a concurrent first sign-in (or a registration) for the same identity. If the login now exists, that request
            // created it and this one signs in; otherwise the address was taken by a password account in the meantime.
            linked = await _users.FindByLoginAsync(AuthProviders.Google, identity.Subject);
            if (linked is null)
            {
                throw _options.Failures.AccountExistsUsePassword.ToException();
            }

            return await SignInLinkedAsync(linked, rememberMe, ct);
        }

        return await _sessions.IssueAsync(created, rememberMe, "GoogleLogin", ct);
    }

    /// <summary>Links a Google account to a signed-in account. The two addresses need not match.</summary>
    /// <param name="userId">The account, as the key's text.</param>
    /// <param name="idToken">The ID token from the app's Google sign-in.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="AppException">
    /// No such account (<see cref="AuthFailures.UserNotFound"/>), a Microsoft account (<see cref="AuthFailures.MicrosoftAccount"/>, refused
    /// before Google is asked), a refused token, an unverified address, an account that already has a Google login, or a Google account that is
    /// linked to another user (<see cref="AuthFailures.GoogleAlreadyLinked"/>).
    /// </exception>
    public async Task LinkAsync(string userId, string idToken, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId) ?? throw _options.Failures.UserNotFound.ToException();

        // Refused before Google is asked: a Microsoft account signs in with Microsoft, and a Google link would be a second way in.
        if (await IsMicrosoftAccountAsync(user))
        {
            throw _options.Failures.MicrosoftAccount.ToException();
        }

        var identity = await _validator.ValidateAsync(idToken, ct) ?? throw _options.Failures.GoogleTokenInvalid.ToException();
        if (!identity.EmailVerified)
        {
            throw _options.Failures.GoogleEmailUnverified.ToException();
        }

        if (await HasGoogleLoginAsync(user) || await _users.FindByLoginAsync(AuthProviders.Google, identity.Subject) is not null)
        {
            throw _options.Failures.GoogleAlreadyLinked.ToException();
        }

        var result = await _users.AddLoginAsync(user, new UserLoginInfo(AuthProviders.Google, identity.Subject, AuthProviders.Google));
        if (!result.Succeeded)
        {
            throw Unclassified(result.Errors);
        }

        _audit.Add(new AuthAuditEntry(userId, "GoogleLinked", "User", userId, Now()));
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Removes the Google login. Refused when it is the only way to sign in.</summary>
    /// <param name="userId">The account, as the key's text.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="AppException">
    /// No such account (<see cref="AuthFailures.UserNotFound"/>), the account has no password (<see cref="AuthFailures.LastSignInMethod"/>), or
    /// no Google login is on it (<see cref="AuthFailures.GoogleNotLinked"/>).
    /// </exception>
    public async Task UnlinkAsync(string userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId) ?? throw _options.Failures.UserNotFound.ToException();

        if (!await _users.HasPasswordAsync(user))
        {
            throw _options.Failures.LastSignInMethod.ToException();
        }

        var google = (await _users.GetLoginsAsync(user)).FirstOrDefault(l => l.LoginProvider == AuthProviders.Google)
            ?? throw _options.Failures.GoogleNotLinked.ToException();

        var result = await _users.RemoveLoginAsync(user, google.LoginProvider, google.ProviderKey);
        if (!result.Succeeded)
        {
            throw Unclassified(result.Errors);
        }

        _audit.Add(new AuthAuditEntry(userId, "GoogleUnlinked", "User", userId, Now()));
        await _db.SaveChangesAsync(ct);
    }

    private async Task<IssuedSession> SignInLinkedAsync(TUser user, bool rememberMe, CancellationToken ct)
    {
        // The same answer the password path gives, so a locked account does not read differently here.
        if (await _users.IsLockedOutAsync(user))
        {
            throw _options.Failures.InvalidCredentials.ToException();
        }

        return await _sessions.IssueAsync(user, rememberMe, "GoogleLogin", ct);
    }

    /// <summary>
    /// Creates the account for a first Google sign-in, with its role, its login and the app's rows, in one transaction. Returns null when a
    /// concurrent request won the race for the same address or login, after rolling back.
    /// </summary>
    private async Task<TUser?> TryCreateAsync(GoogleIdentity identity, CancellationToken ct)
    {
        TUser? created = null;

        // Inside the execution strategy, so a transient fault re-runs the whole creation from a clean tracker.
        var attempt = 0;
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                _db.ChangeTracker.Clear();
            }

            created = null;

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // No password: the account signs in with Google until the person sets one. Google verified the address, so it is confirmed.
                var user = new TUser
                {
                    Email = identity.Email,
                    UserName = identity.Email,
                    EmailConfirmed = true,
                    FullName = string.IsNullOrWhiteSpace(identity.Name) ? identity.Email : identity.Name,
                };

                var createResult = await _users.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    if (createResult.Errors.All(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
                    {
                        await AbandonLostRaceAsync(transaction, ct);
                        return;
                    }

                    throw Unclassified(createResult.Errors);
                }

                var role = _google.NewAccountRole;
                if (!string.IsNullOrWhiteSpace(role))
                {
                    var roleResult = await _users.AddToRoleAsync(user, role);
                    if (!roleResult.Succeeded)
                    {
                        throw Unclassified(roleResult.Errors);
                    }
                }

                var loginResult = await _users.AddLoginAsync(user, new UserLoginInfo(AuthProviders.Google, identity.Subject, AuthProviders.Google));
                if (!loginResult.Succeeded)
                {
                    throw Unclassified(loginResult.Errors);
                }

                await _newAccount.AddRowsAsync(user, ct);

                var id = AuthKey.Text(user.Id);
                _audit.Add(new AuthAuditEntry(id, "GoogleAccountCreated", "User", id, Now()));
                await _db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
                created = user;
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

        return created;
    }

    private async Task AbandonLostRaceAsync(IDbContextTransaction transaction, CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);

        // The rolled-back rows are still tracked; left there, the next save in this request would write them again.
        _db.ChangeTracker.Clear();
    }

    private async Task<bool> HasGoogleLoginAsync(TUser user) =>
        (await _users.GetLoginsAsync(user)).Any(l => l.LoginProvider == AuthProviders.Google);

    private async Task<bool> IsMicrosoftAccountAsync(TUser user) =>
        (await _users.GetLoginsAsync(user)).Any(l => l.LoginProvider == AuthProviders.Microsoft);

    private AppException Unclassified(IEnumerable<IdentityError> errors) =>
        new(_options.Failures.Unclassified.Code, IdentityErrorText.Of(errors), _options.Failures.Unclassified.Status);

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
