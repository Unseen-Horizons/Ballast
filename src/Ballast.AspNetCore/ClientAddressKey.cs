// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Sockets;

namespace Ballast.AspNetCore;

/// <summary>
/// The address part of a client's rate-limit partition key (the rate limiter writes <c>ip:{For(address)}</c>). An IPv4 address,
/// or an IPv4 address inside an IPv6 mapping, is its own key. Any other IPv6 address is keyed by its /64 prefix, because a home
/// router or a mobile carrier hands out a whole /64: keyed by the full address, one person could take a fresh bucket with each
/// address they were given.
/// </summary>
public static class ClientAddressKey
{
    /// <summary>
    /// The key of a client address. A missing address is keyed as <c>unknown</c>, so every request without one shares one bucket.
    /// </summary>
    /// <param name="address">The client's address, or null.</param>
    /// <returns>The key. It is never null.</returns>
    public static string For(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        return Convert.ToHexString(address.GetAddressBytes(), 0, 8) + "/64";
    }
}
