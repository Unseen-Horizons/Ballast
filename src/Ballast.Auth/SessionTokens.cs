// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.Auth;

/// <summary>
/// Makes the tokens of a session: the short access token (a signed JWT) and the refresh token that goes with it. A refresh token is
/// 64 random bytes in base64, and only its SHA-256 is stored; the raw value is given to the client once and never kept.
/// </summary>
/// <remarks>
/// The format is fixed, because clients depend on it: changing the refresh token format, the lifetimes or a claim name
/// signs every rider out. Remember-me is not a flag on the row: it is read back from the row's lifetime (expiry minus creation).
/// </remarks>
public sealed class SessionTokens<TUser, TKey>
    where TUser : IdentityUser<TKey>, IAuthProfile
    where TKey : IEquatable<TKey>
{
    private readonly AuthOptions _options;
    private readonly UserManager<TUser> _users;
    private readonly IAccessTokenClaims<TUser> _extraClaims;
    private readonly TimeProvider _time;

    /// <summary>Creates the token maker.</summary>
    /// <param name="options">The validated options.</param>
    /// <param name="users">The user manager, for the user's roles.</param>
    /// <param name="extraClaims">The app's extra claims, or an empty set.</param>
    /// <param name="time">The clock for expiry. The system clock in an app.</param>
    public SessionTokens(AuthOptions options, UserManager<TUser> users, IAccessTokenClaims<TUser> extraClaims, TimeProvider time)
    {
        _options = options;
        _users = users;
        _extraClaims = extraClaims;
        _time = time;
    }

    /// <summary>
    /// Signs an access token for a user. It carries the subject, the address, the display name, a unique id, the not-before time,
    /// one role claim per role, the security stamp and whatever the app's <see cref="IAccessTokenClaims{TUser}"/> adds. It expires
    /// after <see cref="AuthOptions.AccessTokenMinutes"/> minutes.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The signed token.</returns>
    /// <exception cref="InvalidOperationException">The signing key is not valid for HMAC-SHA256 (the options check refuses it at startup).</exception>
    public async Task<string> CreateAccessTokenAsync(TUser user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = _time.GetUtcNow().UtcDateTime;
        var names = _options.Claims;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(names.Subject, AuthKey.Text(user.Id)),
            new(names.GivenName, user.FullName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Nbf, EpochTime.GetIntDate(now).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new(names.SecurityStamp, user.SecurityStamp ?? string.Empty),
        };

        // An account with no address has no e-mail claim, rather than an empty one.
        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(names.Email, user.Email));
        }

        foreach (var role in await _users.GetRolesAsync(user))
        {
            claims.Add(new Claim(names.Role, role));
        }

        claims.AddRange(await _extraClaims.ClaimsForAsync(user, ct));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = now.AddMinutes(_options.AccessTokenMinutes),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = creds,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>A new raw refresh token: 64 random bytes in base64, which is what the client holds.</summary>
    /// <returns>The raw token.</returns>
    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    /// <summary>The stored form of a raw refresh token: the base64 SHA-256 of its UTF-8 bytes. Stored and compared; never reversed.</summary>
    /// <param name="rawToken">The raw token the client presented or was given.</param>
    /// <returns>The hash, 44 characters.</returns>
    public string HashRefreshToken(string rawToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    /// <summary>When a refresh token issued now expires: the remember-me lifetime, or the standard one.</summary>
    /// <param name="rememberMe">Whether the sign-in asked to be remembered.</param>
    /// <returns>The expiry, in UTC.</returns>
    public DateTime RefreshTokenExpiresAtUtc(bool rememberMe)
    {
        var days = rememberMe ? _options.RefreshTokenDaysRememberMe : _options.RefreshTokenDays;
        return _time.GetUtcNow().UtcDateTime.AddDays(days);
    }

    /// <summary>
    /// Whether a stored refresh token was issued for a remember-me sign-in: its lifetime is longer than the standard one. Both times are
    /// UTC. A refreshed token keeps the sign-in's kind, so a remember-me session stays one.
    /// </summary>
    /// <param name="createdAtUtc">When the token was created.</param>
    /// <param name="expiresAtUtc">When it expires.</param>
    /// <returns>True for a remember-me token.</returns>
    public bool IsRememberMeRefreshToken(DateTime createdAtUtc, DateTime expiresAtUtc) =>
        expiresAtUtc - createdAtUtc > TimeSpan.FromDays(_options.RefreshTokenDays);
}
