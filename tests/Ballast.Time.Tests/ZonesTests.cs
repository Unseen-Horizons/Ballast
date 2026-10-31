// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time.Tests;

public class ZonesTests
{
    [Fact]
    public void Find_resolves_an_iana_id()
    {
        Assert.Equal("Europe/Zagreb", Zones.Find("Europe/Zagreb")?.Id);
    }

    [Fact]
    public void Find_ignores_surrounding_whitespace()
    {
        Assert.Equal("Europe/Zagreb", Zones.Find("  Europe/Zagreb\r\n")?.Id);
    }

    [Fact]
    public void Find_resolves_a_windows_id()
    {
        Assert.NotNull(Zones.Find("Central European Standard Time"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_gives_null_for_a_null_or_blank_id(string? zoneId)
    {
        Assert.Null(Zones.Find(zoneId));
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("Not/AZone")]
    [InlineData("Europe/Zagrebb")]
    public void Find_gives_null_for_an_id_this_machine_does_not_know(string zoneId)
    {
        Assert.Null(Zones.Find(zoneId));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("Europe/../Zagreb")]
    [InlineData("/Europe/Zagreb")]
    [InlineData(@"Europe\Zagreb")]
    [InlineData(@"C:\Windows\System32")]
    public void Find_refuses_an_id_shaped_like_a_path(string zoneId)
    {
        Assert.Null(Zones.Find(zoneId));
    }

    [Theory]
    [InlineData("Europe/Zagreb\0")]
    [InlineData("Europe/Zagreb;Europe/London")]
    [InlineData("Ünknown/Zone")]
    [InlineData("1Europe/Zagreb")]
    public void Find_refuses_a_character_outside_the_zone_id_shape(string zoneId)
    {
        Assert.Null(Zones.Find(zoneId));
    }

    [Fact]
    public void Find_refuses_an_id_longer_than_64_characters()
    {
        Assert.Null(Zones.Find("A" + new string('b', 64)));
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\r\n\t")]
    [InlineData("Europe/Zagreb/\0/")]
    public void Find_never_throws_on_hostile_input(string zoneId)
    {
        Assert.Null(Zones.Find(zoneId));
    }

    [Fact]
    public void Find_and_canonical_never_throw_on_a_lone_surrogate()
    {
        // Built here, not in InlineData: the test framework would replace a lone surrogate with U+FFFD in the argument.
        var loneSurrogate = "Europe/" + (char)0xD800;

        Assert.Null(Zones.Find(loneSurrogate));
        Assert.Null(Zones.Canonical(loneSurrogate));
    }

    [Fact]
    public void Canonical_keeps_an_iana_id_as_written()
    {
        Assert.Equal("Europe/Zagreb", Zones.Canonical("Europe/Zagreb"));
    }

    [Fact]
    public void Canonical_removes_surrounding_whitespace()
    {
        Assert.Equal("Europe/Zagreb", Zones.Canonical("  Europe/Zagreb "));
    }

    [Fact]
    public void Canonical_converts_a_windows_id_to_the_iana_id_this_machine_maps_it_to()
    {
        var canonical = Zones.Canonical("Central European Standard Time");

        Assert.NotNull(canonical);
        Assert.Contains('/', canonical);
        Assert.NotNull(Zones.Find(canonical));
        Assert.Equal(canonical, Zones.Canonical(canonical));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Canonical_is_null_for_an_id_that_does_not_resolve(string? zoneId)
    {
        Assert.Null(Zones.Canonical(zoneId));
    }
}
