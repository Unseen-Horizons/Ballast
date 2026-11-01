// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;

namespace Ballast.AspNetCore;

/// <summary>The partition keys a rate limit can use.</summary>
public static class RateLimitKeys
{
    /// <summary>
    /// The key of a caller by its address alone: <c>ip:{address}</c>, with an IPv6 address keyed by its /64 (see
    /// <see cref="ClientAddressKey"/>). Use it when a limit must hold per address whoever is signed in.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>The key. It is never null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static string ForAddress(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return "ip:" + ClientAddressKey.For(context.Connection.RemoteIpAddress);
    }

    /// <summary>
    /// The key of a signed-in caller: <c>user:{id}</c>, where the id is the first of <paramref name="userIdClaimTypes"/> the caller holds.
    /// A caller that holds none is keyed by its address, so anonymous callers are limited per address.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="userIdClaimTypes">The claim types that hold the user id, tried in order.</param>
    /// <returns>The key. It is never null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="userIdClaimTypes"/> is null.</exception>
    public static string ForUserOrAddress(HttpContext context, IEnumerable<string> userIdClaimTypes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(userIdClaimTypes);

        foreach (var claimType in userIdClaimTypes)
        {
            var id = context.User.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                return "user:" + id;
            }
        }

        return ForAddress(context);
    }
}
