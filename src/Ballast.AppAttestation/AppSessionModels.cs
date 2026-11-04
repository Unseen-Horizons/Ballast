// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>A freshly signed session: the token, its id and when it ends.</summary>
/// <param name="Token">The signed token.</param>
/// <param name="SessionId">The token's <c>sid</c> claim.</param>
/// <param name="ExpiresAt">When the session ends, in UTC.</param>
public sealed record IssuedAppSession(string Token, string SessionId, DateTimeOffset ExpiresAt);

/// <summary>A validated session.</summary>
/// <param name="SessionId">The session's id. It names a session, never a person.</param>
/// <param name="Attestation">How the install was vouched for: <see cref="Attestation"/>.</param>
/// <param name="Platform">The platform the install reported when it created the session.</param>
/// <param name="ExpiresAt">When the session ends, in UTC.</param>
public sealed record AppSessionInfo(string SessionId, string Attestation, string Platform, DateTimeOffset ExpiresAt);

/// <summary>
/// Why a presented token is not a session, or the session itself. <see cref="Failure"/> is for a log line only: it never reaches
/// the caller, who is told "no session" and nothing about why.
/// </summary>
/// <param name="Session">The session, or null when the token is not one.</param>
/// <param name="Failure">The reason it is not, for a log line, or null when it is a session.</param>
public sealed record AppSessionValidation(AppSessionInfo? Session, string? Failure)
{
    /// <summary>A valid session.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The validation.</returns>
    public static AppSessionValidation Ok(AppSessionInfo session) => new(session, null);

    /// <summary>A token that is not a session.</summary>
    /// <param name="failure">The reason, for a log line.</param>
    /// <returns>The validation.</returns>
    public static AppSessionValidation Fail(string failure) => new(null, failure);
}

/// <summary>A session found on the current request, with its raw token (a web ticket carries the token into the cookie).</summary>
/// <param name="Info">The session.</param>
/// <param name="Token">The token as it was presented.</param>
public sealed record AppSessionFeature(AppSessionInfo Info, string Token);

/// <summary>What a web ticket stands for: the session token to put in the cookie, and when it ends.</summary>
/// <param name="Token">The session token.</param>
/// <param name="ExpiresAt">When the session ends, in UTC.</param>
public sealed record WebTicketGrant(string Token, DateTimeOffset ExpiresAt);

/// <summary>What a redeemed web ticket yields: where to send the browser, and the cookie to set (if any).</summary>
/// <param name="RedirectTo">The validated path to send the browser to.</param>
/// <param name="Token">The session token for the cookie, or null when the ticket was not redeemable.</param>
/// <param name="ExpiresAt">When that session ends: the cookie ends with it. Null with no cookie.</param>
/// <param name="MaxAge">The session's remaining lifetime now, which is the cookie's. Null with no cookie.</param>
public sealed record WebTicketRedemption(string RedirectTo, string? Token, DateTimeOffset? ExpiresAt, TimeSpan? MaxAge);
