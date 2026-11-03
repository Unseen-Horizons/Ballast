// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.AppAttestation;

/// <summary>
/// Signs session tokens: HS256 over <see cref="AppSessionOptions.Key"/>, with the issuer and audience the app sets in
/// <see cref="AppAttestationSetup"/>, and the claims <c>sid</c>, <c>att</c> and <c>plat</c> and nothing else. There is no user,
/// no role and no refresh token. The lifetime is <see cref="AppSessionOptions.LifetimeHours"/> and nothing is stored, so the
/// only way to end a session early is to change the key. The checking half is <see cref="AppSessionValidator"/>.
/// <para>
/// This is not a user's access token and does not touch one: user tokens, their refresh tokens and their rotation are a
/// different pipeline.
/// </para>
/// </summary>
public sealed class AppSessionTokenService
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials;
    private readonly TimeSpan _lifetime;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeProvider _clock;

    /// <summary>Creates the signer.</summary>
    /// <param name="options">The session options. The key and the lifetime are read here.</param>
    /// <param name="setup">The setup, whose issuer and audience the token carries.</param>
    /// <param name="clock">The clock the issue and expiry times come from.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public AppSessionTokenService(IOptions<AppSessionOptions> options, AppAttestationSetup setup, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(clock);

        _credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.Key)), SecurityAlgorithms.HmacSha256);
        _lifetime = options.Value.Lifetime;
        _issuer = setup.Issuer;
        _audience = setup.Audience;
        _clock = clock;
    }

    /// <summary>Signs a new session.</summary>
    /// <param name="attestation">How the install was vouched for: <see cref="Attestation"/>.</param>
    /// <param name="platform">The platform the install reported.</param>
    /// <returns>The token, its id and when it ends.</returns>
    public IssuedAppSession Issue(string attestation, string platform)
    {
        var now = _clock.GetUtcNow();
        var expires = now.Add(_lifetime);
        var sessionId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(AppAttestationDefaults.SessionIdClaim, sessionId),
                new Claim(AppAttestationDefaults.AttestationClaim, attestation),
                new Claim(AppAttestationDefaults.PlatformClaim, platform),
            ]),
            Issuer = _issuer,
            Audience = _audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials,
        });

        return new IssuedAppSession(token, sessionId, expires);
    }
}
