// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;

namespace Ballast.AppAttestation;

/// <summary>
/// The cookie a redeemed web ticket sets: the session token, <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Strict</c>, path <c>/</c>,
/// for exactly the session's remaining lifetime. A script on the page cannot read it, and no other site's page sends it.
/// </summary>
public static class AppSessionCookie
{
    /// <summary>The cookie options for a session token that ends at <paramref name="expires"/>.</summary>
    /// <param name="expires">When the session ends, in UTC. The cookie ends with it.</param>
    /// <param name="maxAge">The session's remaining lifetime now.</param>
    /// <returns>The options.</returns>
    public static CookieOptions CreateOptions(DateTimeOffset expires, TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = maxAge,
        Expires = expires,
    };

    /// <summary>
    /// Sets the session cookie from a redemption. A redemption with no token (an unknown, used or expired ticket) sets nothing, and the
    /// caller still redirects: the page loads, and when its data then needs a session it asks the app for a new one.
    /// </summary>
    /// <param name="response">The response to set the cookie on.</param>
    /// <param name="setup">The setup, which names the cookie.</param>
    /// <param name="redemption">What the redemption yielded.</param>
    /// <returns>True when a cookie was set.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public static bool Write(HttpResponse response, AppAttestationSetup setup, WebTicketRedemption redemption)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(redemption);

        if (redemption.Token is null || redemption.ExpiresAt is not { } expires || redemption.MaxAge is not { } maxAge)
            return false;

        response.Cookies.Append(setup.CookieName, redemption.Token, CreateOptions(expires, maxAge));
        return true;
    }
}
