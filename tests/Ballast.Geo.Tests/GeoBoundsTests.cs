// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Geo.Tests;

/// <summary>The usable-position rule. The cases are those of the stop check the library was ported from.</summary>
public class GeoBoundsTests
{
    [Theory]
    [InlineData(double.NaN, 15.9619)]
    [InlineData(45.8131, double.NaN)]
    [InlineData(double.PositiveInfinity, 15.9619)]
    [InlineData(45.8131, double.NegativeInfinity)]
    [InlineData(91.0, 15.9619)]
    [InlineData(-90.5, 15.9619)]
    [InlineData(45.8131, 181.0)]
    [InlineData(0.0, 0.0)]
    public void An_unusable_coordinate_is_rejected(double latitude, double longitude)
        => Assert.False(GeoBounds.IsUsable(latitude, longitude));

    [Theory]
    [InlineData(45.8131, 15.9619)]
    [InlineData(-90.0, -180.0)]
    [InlineData(90.0, 180.0)]
    [InlineData(0.0, 15.9619)]
    [InlineData(45.8131, 0.0)]
    public void A_usable_coordinate_is_accepted(double latitude, double longitude)
        => Assert.True(GeoBounds.IsUsable(latitude, longitude));

    [Fact]
    public void Negative_zero_is_refused_as_null_island()
    {
        Assert.False(GeoBounds.IsUsable(-0.0, 0.0));
        Assert.False(GeoBounds.IsUsable(0.0, -0.0));
    }
}
