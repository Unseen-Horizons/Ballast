// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

namespace Ballast.Maui.Auth;

/// <summary>
/// What a refresh-token call came back with. Only a refusal ends the session: a busy, failing or unreachable server is no
/// verdict on the token, so the stored tokens are kept.
/// </summary>
public enum RefreshOutcome
{
    /// <summary>A new token pair is stored, or another call refreshed while this one waited.</summary>
    Refreshed,

    /// <summary>The server refused the refresh token (401). The session is over.</summary>
    Rejected,

    /// <summary>
    /// No verdict came back: no network, a timeout, or a busy or failing server (408, 429, 5xx, anything else). The stored
    /// tokens may still be good, so nothing is wiped.
    /// </summary>
    Unavailable,
}

/// <summary>The rules that turn a refresh reply into an outcome.</summary>
public static class RefreshOutcomeRules
{
    /// <summary>Maps the status of a refresh reply to an outcome: 2xx is refreshed, 401 is refused, anything else is no verdict.</summary>
    public static RefreshOutcome FromStatus(HttpStatusCode status)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
            return RefreshOutcome.Refreshed;
        if (status == HttpStatusCode.Unauthorized)
            return RefreshOutcome.Rejected;
        return RefreshOutcome.Unavailable;
    }

    /// <summary>Only a refusal ends the session. An unavailable server never signs the rider out.</summary>
    public static bool ShouldSignOut(RefreshOutcome outcome) => outcome == RefreshOutcome.Rejected;
}
