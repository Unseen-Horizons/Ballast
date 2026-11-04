// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Google.Apis.Auth;

namespace Ballast.Auth;

/// <summary>
/// Checks a Google ID token's signature against Google's published keys, and returns the payload Google signed. The validator takes one of
/// these, so tests can answer for Google without the network. The default is <see cref="GoogleJsonWebSignatureVerifier"/>.
/// </summary>
public interface IGoogleTokenVerifier
{
    /// <summary>Checks the token and returns its payload.</summary>
    /// <param name="idToken">The ID token, as Google's sign-in gave it.</param>
    /// <param name="settings">The audience the token must be for.</param>
    /// <returns>The payload. Throws <see cref="Google.Apis.Auth.InvalidJwtException"/> for a token Google does not vouch for.</returns>
    Task<GoogleJsonWebSignature.Payload> VerifyAsync(string idToken, GoogleJsonWebSignature.ValidationSettings settings);
}

/// <summary>The default verifier: Google's own library, which checks the signature, the issuer, the expiry and the audience.</summary>
public sealed class GoogleJsonWebSignatureVerifier : IGoogleTokenVerifier
{
    /// <inheritdoc />
    public Task<GoogleJsonWebSignature.Payload> VerifyAsync(string idToken, GoogleJsonWebSignature.ValidationSettings settings) =>
        GoogleJsonWebSignature.ValidateAsync(idToken, settings);
}
