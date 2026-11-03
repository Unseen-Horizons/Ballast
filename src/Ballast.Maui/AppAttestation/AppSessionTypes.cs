// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.AppAttestation;

/// <summary>The codes the server answers with when an app cannot be verified. Spelled the way the server's own codes are.</summary>
public static class AppSessionCodes
{
    /// <summary>401: the call needs an app session, and the one it carried is missing or no longer valid.</summary>
    public const string SessionRequired = "APP_SESSION_REQUIRED";

    /// <summary>403: Play (or the developer path) did not vouch for this install.</summary>
    public const string AttestationFailed = "APP_ATTESTATION_FAILED";

    /// <summary>503: the server could not ask Google. Worth trying again shortly.</summary>
    public const string AttestationUnavailable = "APP_ATTESTATION_UNAVAILABLE";
}

/// <summary>What kind of failure a session attempt hit.</summary>
public enum AppSessionFailureKind
{
    /// <summary>403: Play (or the developer path) did not vouch for this install.</summary>
    Refused,

    /// <summary>503: the server could not ask Google. Worth trying again shortly.</summary>
    Unavailable,

    /// <summary>The server could not be reached at all.</summary>
    Unreachable,

    /// <summary>Anything else: a status or body the library does not recognise.</summary>
    Other,
}

/// <summary>
/// Why a session could not be had. <see cref="Code"/> is the server's code, or one the library uses for a failure it never reached
/// the server for.
/// </summary>
/// <param name="Kind">What kind of failure it was.</param>
/// <param name="Code">The server's code, or the library's own.</param>
/// <param name="Message">The server's title, or a short reason.</param>
public sealed record AppSessionFailure(AppSessionFailureKind Kind, string Code, string Message);

/// <summary>A web ticket, or the reason there is none.</summary>
/// <param name="Ticket">The one-use ticket, or null.</param>
/// <param name="Failure">Why there is no ticket, or null when there is one.</param>
public sealed record WebTicketResult(string? Ticket, AppSessionFailure? Failure);
