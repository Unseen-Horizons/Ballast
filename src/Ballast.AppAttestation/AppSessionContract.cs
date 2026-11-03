// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>The body an install posts to create its session. The JSON names are <c>platform</c>, <c>installId</c>, <c>nonce</c> and <c>integrityToken</c>.</summary>
/// <param name="Platform">One of <see cref="AppAttestationSetup.Platforms"/> (<c>android</c>, <c>ios</c>, <c>maccatalyst</c> or <c>windows</c> by default).</param>
/// <param name="InstallId">A random id the app keeps for this install. Only a hash of it is logged.</param>
/// <param name="Nonce">The nonce the server issued, which the integrity token was requested with.</param>
/// <param name="IntegrityToken">The Play Integrity token. Absent when the platform cannot attest (Windows, iOS or an emulator).</param>
public record CreateAppSessionRequest(string Platform, string InstallId, string Nonce, string? IntegrityToken);

/// <summary>The answer to a nonce request: a single-use nonce to request the integrity token with.</summary>
public class AppNonceResponse
{
    /// <summary>The nonce, 16 random bytes as unpadded base64url.</summary>
    public string Nonce { get; set; } = string.Empty;
}

/// <summary>The answer to a session request: the session token and how long it lasts.</summary>
public class AppSessionResponse
{
    /// <summary>The session token, sent back in the session header.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>The session's remaining lifetime in seconds, at the moment it was issued.</summary>
    public int ExpiresInSeconds { get; set; }
}

/// <summary>The answer to a web-ticket request: a single-use ticket to redeem in the web view's address.</summary>
public class AppWebTicketResponse
{
    /// <summary>The ticket. It works once, for sixty seconds by default.</summary>
    public string Ticket { get; set; } = string.Empty;
}
