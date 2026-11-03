// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;
using System.Text;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.AppAttestation;

/// <summary>
/// Validates a session token. It holds no state and touches no store: a session is stateless, so the signature, the issuer,
/// the audience and the lifetime are the whole check.
/// </summary>
public sealed class AppSessionValidator
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _parameters;
    private readonly bool _acceptUnattested;

    /// <summary>Creates the validator.</summary>
    /// <param name="key">The signing key, at least 32 bytes. The start-up check refuses any other key before this runs.</param>
    /// <param name="issuer">The issuer a session must carry.</param>
    /// <param name="audience">The audience a session must carry. It must differ from the user tokens' audience.</param>
    /// <param name="acceptUnattested">
    /// Whether a session whose attestation is <see cref="Attestation.None"/> validates. True only in Development: an unattested
    /// session that somehow exists elsewhere is refused rather than trusted.
    /// </param>
    /// <param name="timeProvider">The clock for the lifetime check. Tests pass a fixed one. Null for the system clock.</param>
    /// <exception cref="ArgumentException">The key, the issuer or the audience is blank.</exception>
    public AppSessionValidator(string key, string issuer, string audience, bool acceptUnattested, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        _acceptUnattested = acceptUnattested;
        var clock = timeProvider ?? TimeProvider.System;
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),

            // Only the algorithm the issuer uses: a token naming another one is refused outright.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var skew = TimeSpan.FromSeconds(30);
                return expires is not null && now <= expires.Value.Add(skew) && (notBefore is null || now >= notBefore.Value.Subtract(skew));
            },
        };
    }

    /// <summary>Validates a token.</summary>
    /// <param name="token">The token as it was presented. Null, blank or malformed is not a session.</param>
    /// <returns>The session, or the reason it is not one.</returns>
    public async Task<AppSessionValidation> ValidateAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return AppSessionValidation.Fail("no token");

        TokenValidationResult result;
        try
        {
            result = await _handler.ValidateTokenAsync(token, _parameters);
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
        {
            return AppSessionValidation.Fail("malformed token");
        }

        if (!result.IsValid)
            return AppSessionValidation.Fail(result.Exception?.GetType().Name ?? "invalid token");

        var claims = result.ClaimsIdentity.Claims.ToList();
        string? Find(string type) => claims.FirstOrDefault(c => c.Type == type)?.Value;

        var sid = Find(AppAttestationDefaults.SessionIdClaim);
        var att = Find(AppAttestationDefaults.AttestationClaim);
        var plat = Find(AppAttestationDefaults.PlatformClaim);
        if (string.IsNullOrEmpty(sid) || !Attestation.IsKnown(att) || string.IsNullOrEmpty(plat))
            return AppSessionValidation.Fail("missing claims");

        if (att == Attestation.None && !_acceptUnattested)
            return AppSessionValidation.Fail("unattested session outside Development");

        var exp = result.SecurityToken is JsonWebToken jwt ? new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero) : DateTimeOffset.MinValue;
        return AppSessionValidation.Ok(new AppSessionInfo(sid, att!, plat, exp));
    }

    /// <summary>The principal a valid session signs in as. It carries no <c>sub</c>, so nothing mistakes it for a user.</summary>
    /// <param name="session">The session.</param>
    /// <returns>A principal with the session's three claims, under the scheme's name.</returns>
    public static ClaimsPrincipal ToPrincipal(AppSessionInfo session)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(AppAttestationDefaults.SessionIdClaim, session.SessionId),
            new Claim(AppAttestationDefaults.AttestationClaim, session.Attestation),
            new Claim(AppAttestationDefaults.PlatformClaim, session.Platform),
        ], AppAttestationDefaults.SchemeName);
        return new ClaimsPrincipal(identity);
    }
}
