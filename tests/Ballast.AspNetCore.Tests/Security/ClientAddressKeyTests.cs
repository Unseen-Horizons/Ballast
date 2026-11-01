// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

namespace Ballast.AspNetCore.Tests.Security;

/// <summary>A rate-limit key is per person for IPv6 (the /64), and unchanged for IPv4.</summary>
public class ClientAddressKeyTests
{
    [Fact]
    public void Two_addresses_in_one_slash64_share_a_key()
    {
        Assert.Equal(
            ClientAddressKey.For(IPAddress.Parse("2001:db8:1:2::1")),
            ClientAddressKey.For(IPAddress.Parse("2001:db8:1:2::ffff")));
    }

    [Fact]
    public void Addresses_in_different_slash64s_get_different_keys()
    {
        Assert.NotEqual(
            ClientAddressKey.For(IPAddress.Parse("2001:db8:1:2::1")),
            ClientAddressKey.For(IPAddress.Parse("2001:db8:1:3::1")));
    }

    [Fact]
    public void An_ipv4_mapped_ipv6_address_is_keyed_as_its_ipv4_address()
    {
        Assert.Equal("1.2.3.4", ClientAddressKey.For(IPAddress.Parse("::ffff:1.2.3.4")));
    }

    [Fact]
    public void An_ipv4_address_is_keyed_as_itself()
    {
        Assert.Equal("1.2.3.4", ClientAddressKey.For(IPAddress.Parse("1.2.3.4")));
    }

    [Fact]
    public void A_missing_address_is_unknown()
    {
        Assert.Equal("unknown", ClientAddressKey.For(null));
    }

    [Fact]
    public void The_ipv6_key_is_the_first_64_bits_in_hex_with_a_slash_64()
    {
        Assert.Equal("20010DB800010002/64", ClientAddressKey.For(IPAddress.Parse("2001:db8:1:2::1")));
    }
}
