// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq.Expressions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth;

/// <summary>
/// Issues, rotates and ends sessions: a signed access token and a refresh token. A refresh is one conditional update, so two requests
/// that present the same token cannot both succeed; a presented token that was already rotated, or that loses that race, revokes the
/// user's whole family. A locked account cannot refresh.
/// </summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRefresh">The app's refresh token entity.</typeparam>
/// <typeparam name="TContext">The app's database context.</typeparam>
public sealed class AuthSessionService<TUser, TKey, TRefresh, TContext>
    where TUser : IdentityUser<TKey>, IAuthProfile
    where TKey : IEquatable<TKey>
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
    where TContext : DbContext
{
    private readonly AuthOptions _options;
    private readonly TContext _db;
    private readonly UserManager<TUser> _users;
    private readonly SessionTokens<TUser, TKey> _tokens;
    private readonly IAuthAudit _audit;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    /// <param name="options">The validated options.</param>
    /// <param name="db">The app's database context. Its refresh token set and its audit rows are written here.</param>
    /// <param name="users">The user manager, which checks the lockout.</param>
    /// <param name="tokens">The token maker.</param>
    /// <param name="audit">Where the audit rows go.</param>
    /// <param name="time">The clock.</param>
    public AuthSessionService(AuthOptions options, TContext db, UserManager<TUser> users, SessionTokens<TUser, TKey> tokens, IAuthAudit audit, TimeProvider time)
    {
        _options = options;
        _db = db;
        _users = users;
        _tokens = tokens;
        _audit = audit;
        _time = time;
    }

    /// <summary>
    /// Starts a session for a user the caller has just proved: adds a refresh token row, writes the audit row and saves both, with any
    /// change the caller made to the user in the same save. The caller builds the answer from the returned session.
    /// </summary>
    /// <param name="user">The proved user.</param>
    /// <param name="rememberMe">Whether the sign-in asked to be remembered (the longer refresh lifetime).</param>
    /// <param name="auditAction">The audit action, such as <c>Login</c>, <c>GoogleLogin</c> or <c>MicrosoftLogin</c>.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The access token and the raw refresh token.</returns>
    public async Task<IssuedSession> IssueAsync(TUser user, bool rememberMe, string auditAction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditAction);

        var now = _time.GetUtcNow().UtcDateTime;
        var accessToken = await _tokens.CreateAccessTokenAsync(user, ct);
        var rawRefreshToken = _tokens.GenerateRefreshToken();
        var expiresAt = _tokens.RefreshTokenExpiresAtUtc(rememberMe);

        _db.Set<TRefresh>().Add(new TRefresh
        {
            Token = _tokens.HashRefreshToken(rawRefreshToken),
            UserId = user.Id,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });

        var id = AuthKey.Text(user.Id);
        _audit.Add(new AuthAuditEntry(id, auditAction, "User", id, now, NewValues: $"RememberMe:{rememberMe}"));
        await _db.SaveChangesAsync(ct);

        return new IssuedSession(accessToken, rawRefreshToken, expiresAt);
    }

    /// <summary>
    /// Rotates a refresh token: the presented token is retired and a successor is issued, with a new access token. A replayed token revokes
    /// the user's whole family. A token whose account is locked is refused.
    /// </summary>
    /// <param name="rawRefreshToken">The raw token the client presents.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The new access token and the successor refresh token.</returns>
    /// <exception cref="Ballast.Errors.AppException">
    /// The token is blank (<see cref="AuthFailures.InvalidRefreshToken"/>), or the token is unknown, expired, revoked, reused, or its
    /// account is locked (<see cref="AuthFailures.RefreshTokenExpired"/>). The last group is one answer, so a caller cannot tell them apart.
    /// </exception>
    public async Task<IssuedSession> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            throw _options.Failures.InvalidRefreshToken.ToException();
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var incomingHash = _tokens.HashRefreshToken(rawRefreshToken);
        var existing = await _db.Set<TRefresh>()
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == incomingHash, ct);

        // Expiry is judged on the injected clock, the same clock that stamped the token, so the two cannot disagree.
        var verdict = RefreshTokenEvaluator.Evaluate(
            found: existing is not null,
            hasReplacement: existing?.ReplacedByToken is not null,
            isActive: existing is not null && existing.RevokedAt is null && now < existing.ExpiresAt);

        if (existing is null || verdict is RefreshTokenVerdict.Invalid)
        {
            throw _options.Failures.RefreshTokenExpired.ToException();
        }

        // A token that was already rotated once is presented again: assume theft and revoke every live token the user has.
        if (verdict is RefreshTokenVerdict.ReuseDetected)
        {
            await RevokeFamilyAsync(existing, "RefreshTokenReuseDetected", ct);
            throw _options.Failures.RefreshTokenExpired.ToException();
        }

        // The token is good, but the account may have changed since it was issued. The lockout also applies here, not only at a password
        // sign-in. The answer is the expired one, so an unauthenticated caller cannot tell a locked account from an unknown token.
        var account = existing.User;
        if (account is null || await _users.IsLockedOutAsync(account))
        {
            _audit.Add(new AuthAuditEntry(AuthKey.Text(existing.UserId), "RefreshRejectedForLockedAccount", "RefreshToken",
                existing.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), _time.GetUtcNow().UtcDateTime));
            await _db.SaveChangesAsync(ct);
            throw _options.Failures.RefreshTokenExpired.ToException();
        }

        var newRawRefreshToken = _tokens.GenerateRefreshToken();
        var newHash = _tokens.HashRefreshToken(newRawRefreshToken);
        var wasRememberMe = _tokens.IsRememberMeRefreshToken(existing.CreatedAt, existing.ExpiresAt);

        // Claim the token before the successor is issued, in one conditional statement. Everything above is a read, and two requests that
        // both read the token before either wrote would both mint a successor, so one refresh token would become two. The WHERE clause is
        // the lock: only the request that updates the row sees a non-zero count.
        var claimed = await _db.Set<TRefresh>()
            .Where(rt => rt.Id == existing.Id && rt.RevokedAt == null && rt.ReplacedByToken == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(rt => rt.RevokedAt, now)
                .SetProperty(rt => rt.ReplacedByToken, newHash), ct);

        if (claimed == 0)
        {
            // Another request rotated this exact token between our read and our write. That is the same event as presenting a rotated token,
            // so it gets the same answer: assume theft. Treating it as benign would reward a stolen token that races the real client.
            await RevokeFamilyAsync(existing, "RefreshTokenReuseDetected", ct);
            throw _options.Failures.RefreshTokenExpired.ToException();
        }

        _db.Set<TRefresh>().Add(new TRefresh
        {
            Token = newHash,
            UserId = existing.UserId,
            CreatedAt = now,
            ExpiresAt = _tokens.RefreshTokenExpiresAtUtc(wasRememberMe),
        });

        _audit.Add(new AuthAuditEntry(AuthKey.Text(existing.UserId), "TokenRefresh", "RefreshToken",
            existing.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), now));
        await _db.SaveChangesAsync(ct);

        var accessToken = await _tokens.CreateAccessTokenAsync(account, ct);
        return new IssuedSession(accessToken, newRawRefreshToken, _tokens.RefreshTokenExpiresAtUtc(wasRememberMe));
    }

    /// <summary>
    /// Ends the session of a presented refresh token: the token is revoked. An unknown or already revoked token is not an error, so a
    /// logout always answers the same way. The caller need not be signed in, because the client often logs out with an expired access token.
    /// </summary>
    /// <param name="rawRefreshToken">The raw token, or null or blank for none.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return;
        }

        var hash = _tokens.HashRefreshToken(rawRefreshToken);
        var existing = await _db.Set<TRefresh>().FirstOrDefaultAsync(rt => rt.Token == hash, ct);
        if (existing is null || existing.RevokedAt.HasValue)
        {
            return;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        existing.RevokedAt = now;
        _audit.Add(new AuthAuditEntry(AuthKey.Text(existing.UserId), "Logout", "RefreshToken",
            existing.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), now));
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Revokes every live refresh token of a user and saves. Used after a password change, so the other sessions end. The access tokens
    /// that are already out end at the stamp check, because the caller changes the stamp first.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task RevokeAllAsync(TKey userId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var live = await _db.Set<TRefresh>()
            .Where(BelongsTo(userId))
            .Where(rt => rt.RevokedAt == null && rt.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in live)
        {
            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Builds the condition "this row belongs to this user" for a key of any type. A generic comparison on the key cannot be written in
    /// C#, and an interface call cannot be translated to SQL, so the comparison is built as an expression tree over the property itself.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The condition.</returns>
    internal static Expression<Func<TRefresh, bool>> BelongsTo(TKey userId)
    {
        var row = Expression.Parameter(typeof(TRefresh), "rt");
        var property = Expression.Property(row, nameof(AuthRefreshTokenBase<TKey, TUser>.UserId));
        // The key is read from a holder, not written into the tree as a literal. EF sends a value read from a constant holder as a SQL
        // parameter, so SQL Server keeps one plan for all users, not one per key.
        var value = Expression.Field(Expression.Constant(new QueryValue<TKey>(userId)), nameof(QueryValue<TKey>.Value));
        return Expression.Lambda<Func<TRefresh, bool>>(Expression.Equal(property, value), row);
    }

    private async Task RevokeFamilyAsync(TRefresh presented, string action, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var family = await _db.Set<TRefresh>()
            .Where(BelongsTo(presented.UserId))
            .Where(rt => rt.RevokedAt == null && rt.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in family)
        {
            token.RevokedAt = now;
        }

        _audit.Add(new AuthAuditEntry(AuthKey.Text(presented.UserId), action, "RefreshToken",
            presented.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), now));
        await _db.SaveChangesAsync(ct);
    }
}
