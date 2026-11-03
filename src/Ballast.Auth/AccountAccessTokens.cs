// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Security.Claims;
using System.Text;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.Auth;

/// <summary>
/// Signs access tokens for an account that is not an Identity user, such as a portal contact. The token has the same key, issuer,
/// audience and claim names as a user's token, so the same bearer pipeline accepts it, and the same access check ends it: a changed
/// security stamp, or a locked or deactivated account, refuses it at the next request. It has no refresh token. Such a session lasts
/// its lifetime, and the app issues a new one when it ends. Scoped: it reads the account through <see cref="IAuthAccountStore"/>.
/// </summary>
/// <remarks>
/// The subject must be the one the app's <see cref="IAuthAccountStore"/> resolves. An app whose Identity user ids and other accounts share
/// one subject space gives the other accounts a prefix, such as <c>contact:42</c>, and its store splits on that prefix.
/// </remarks>
public sealed class AccountAccessTokens
{
    private static readonly string[] ReservedClaimTypes =
    [
        JwtRegisteredClaimNames.Jti,
        JwtRegisteredClaimNames.Nbf,
        JwtRegisteredClaimNames.Exp,
        JwtRegisteredClaimNames.Iss,
        JwtRegisteredClaimNames.Aud,
    ];

    private readonly AuthOptions _options;
    private readonly IAuthAccountStore _accounts;
    private readonly TimeProvider _time;

    /// <summary>Creates the token maker.</summary>
    /// <param name="options">The validated options: the key, the issuer, the audience and the claim names.</param>
    /// <param name="accounts">The store that finds the account's stamp and whether it may sign in.</param>
    /// <param name="time">The clock for the issue and expiry times.</param>
    public AccountAccessTokens(AuthOptions options, IAuthAccountStore accounts, TimeProvider time)
    {
        _options = options;
        _accounts = accounts;
        _time = time;
    }

    /// <summary>
    /// Signs an access token for an account that may sign in now. The token carries the subject, the account's current security stamp, a
    /// unique id, the not-before time, and the extra claims the app passes, such as its auth type and role. It expires after the lifetime.
    /// </summary>
    /// <param name="subject">The subject, as the account store names it.</param>
    /// <param name="lifetime">How long the token is valid. Greater than zero.</param>
    /// <param name="extraClaims">The app's own claims, or null for none. The library's own claims cannot be repeated: the subject, the
    /// security stamp, the unique id, the not-before time, the expiry, the issuer and the audience.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The signed token.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not greater than zero.</exception>
    /// <exception cref="ArgumentException"><paramref name="extraClaims"/> repeats a claim the library sets.</exception>
    /// <exception cref="Ballast.Errors.AppException">
    /// The account does not exist or cannot sign in (<see cref="AuthFailures.InvalidCredentials"/>). The same answer is given for both, so
    /// a caller cannot tell a locked account from an unknown one.
    /// </exception>
    public async Task<string> CreateAsync(string subject, TimeSpan lifetime, IEnumerable<Claim>? extraClaims = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var names = _options.Claims;
        var extra = extraClaims?.ToList() ?? [];
        foreach (var claim in extra)
        {
            if (claim.Type == names.Subject || claim.Type == names.SecurityStamp || Array.IndexOf(ReservedClaimTypes, claim.Type) >= 0)
            {
                throw new ArgumentException($"The claim {claim.Type} is set by the library and cannot be passed in.", nameof(extraClaims));
            }
        }

        var state = await _accounts.FindAsync(subject, ct);
        if (state is null || !state.CanSignIn)
        {
            throw _options.Failures.InvalidCredentials.ToException();
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var claims = new List<Claim>
        {
            new(names.Subject, subject),
            new(names.SecurityStamp, state.SecurityStamp),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Nbf, EpochTime.GetIntDate(now).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
        };
        claims.AddRange(extra);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = now.Add(lifetime),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
