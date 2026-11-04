// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.AppAttestation;

/// <summary>
/// The platform's proof that this is a genuine install of this app on a genuine device, bound to a nonce the server issued.
/// Android asks Google Play Integrity. A platform with no attestation returns no token.
/// </summary>
public interface IAppAttestation
{
    /// <summary>The platform name the server's session request names: <c>android</c>, <c>ios</c>, <c>maccatalyst</c> or <c>windows</c>.</summary>
    string Platform { get; }

    /// <summary>
    /// A token that answers <paramref name="nonce"/>, or null when this platform cannot attest or the request failed. A null token is
    /// still sent to the server, which decides: a Development server issues an unattested session, any other refuses it.
    /// </summary>
    Task<string?> GetTokenAsync(string nonce, CancellationToken ct = default);
}

/// <summary>
/// The attestation on a platform with none. It has no token to offer, and the server's answer (a Development bypass, or a refusal)
/// is what the rider or developer sees. It is not a fallback to an offline substitute.
/// </summary>
public sealed class UnavailableAppAttestation : IAppAttestation
{
    /// <summary>Creates it for a platform.</summary>
    /// <param name="platform">The platform name the server's session request names.</param>
    public UnavailableAppAttestation(string platform) => Platform = platform;

    /// <inheritdoc />
    public string Platform { get; }

    /// <inheritdoc />
    public Task<string?> GetTokenAsync(string nonce, CancellationToken ct = default) => Task.FromResult<string?>(null);
}
