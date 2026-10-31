// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Geo.Tests;

public class GeoBoxTests
{
    private const double CentreLatitude = 45.8131;
    private const double CentreLongitude = 15.9619;

    [Fact]
    public void A_box_holds_every_position_within_its_radius()
    {
        const double radius = 5_000;
        var box = GeoBox.Around(CentreLatitude, CentreLongitude, radius);

        for (var step = 0; step < 24; step++)
        {
            foreach (var fraction in new[] { 0.1, 0.5, 0.999 })
            {
                var (lat, lon) = Destination(CentreLatitude, CentreLongitude, step * 15.0, radius * fraction);

                Assert.True(lat >= box.MinLat && lat <= box.MaxLat, $"latitude {lat} at bearing {step * 15} is outside the box");
                Assert.True(lon >= box.MinLon && lon <= box.MaxLon, $"longitude {lon} at bearing {step * 15} is outside the box");
            }
        }
    }

    [Fact]
    public void The_destination_helper_used_above_is_on_the_radius()
    {
        // Guards the helper: the points the box test uses must really lie at the distances it claims.
        var (lat, lon) = Destination(CentreLatitude, CentreLongitude, 90, 4_000);

        Assert.InRange(GeoDistance.HaversineMeters(CentreLatitude, CentreLongitude, lat, lon), 3_999.9, 4_000.1);
    }

    [Fact]
    public void A_box_of_1_km_is_about_1_km_each_way()
    {
        var box = GeoBox.Around(45, 15, 1000);

        Assert.InRange(box.MaxLat - 45, 0.00899, 0.00900);
        Assert.InRange(45 - box.MinLat, 0.00899, 0.00900);
        Assert.InRange(box.MaxLon - 15, 0.01271, 0.01273);
        Assert.InRange(15 - box.MinLon, 0.01271, 0.01273);
    }

    [Fact]
    public void A_box_near_the_east_edge_is_cut_at_180_and_reports_it()
    {
        var box = GeoBox.Around(0, 179.9, 50_000);

        Assert.Equal(180.0, box.MaxLon);
        Assert.True(box.CrossesAntimeridian);
        Assert.True(box.MinLon < 179.9);
    }

    [Fact]
    public void A_box_near_the_west_edge_is_cut_at_minus_180_and_reports_it()
    {
        var box = GeoBox.Around(0, -179.9, 50_000);

        Assert.Equal(-180.0, box.MinLon);
        Assert.True(box.CrossesAntimeridian);
        Assert.True(box.MaxLon > -179.9);
    }

    [Fact]
    public void A_box_away_from_the_antimeridian_is_not_cut()
    {
        Assert.False(GeoBox.Around(45, 15, 1000).CrossesAntimeridian);
    }

    [Fact]
    public void A_circle_that_reaches_the_north_pole_covers_every_longitude_and_is_not_reported_as_cut()
    {
        var box = GeoBox.Around(89.99, 10, 100_000);

        Assert.Equal(90.0, box.MaxLat);
        Assert.Equal(-180.0, box.MinLon);
        Assert.Equal(180.0, box.MaxLon);
        Assert.False(box.CrossesAntimeridian);
    }

    [Fact]
    public void A_circle_that_reaches_the_south_pole_covers_every_longitude()
    {
        var box = GeoBox.Around(-89.5, 10, 100_000);

        Assert.Equal(-90.0, box.MinLat);
        Assert.Equal(-180.0, box.MinLon);
        Assert.Equal(180.0, box.MaxLon);
        Assert.False(box.CrossesAntimeridian);
    }

    [Fact]
    public void A_huge_radius_covers_the_whole_sphere()
    {
        var box = GeoBox.Around(10, 10, 1e12);

        Assert.Equal(-90.0, box.MinLat);
        Assert.Equal(90.0, box.MaxLat);
        Assert.Equal(-180.0, box.MinLon);
        Assert.Equal(180.0, box.MaxLon);
        Assert.False(box.CrossesAntimeridian);
    }

    [Fact]
    public void A_zero_radius_is_a_box_of_one_point()
    {
        var box = GeoBox.Around(45, 15, 0);

        Assert.Equal(45.0, box.MinLat);
        Assert.Equal(45.0, box.MaxLat);
        Assert.Equal(15.0, box.MinLon);
        Assert.Equal(15.0, box.MaxLon);
        Assert.False(box.CrossesAntimeridian);
    }

    [Fact]
    public void Longitude_reach_widens_with_latitude()
    {
        // At 60 degrees a circle of 100 km reaches about 1.80 degrees east and west, not 0.90 (the reach at the equator).
        var box = GeoBox.Around(60, 0, 100_000);

        Assert.InRange(box.MaxLon, 1.79, 1.81);
        Assert.InRange(-box.MinLon, 1.79, 1.81);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(double.NaN, 15.0)]
    [InlineData(91.0, 15.0)]
    [InlineData(45.0, 181.0)]
    public void An_unusable_centre_throws(double latitude, double longitude)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => GeoBox.Around(latitude, longitude, 100));
        Assert.Equal("latitude", error.ParamName);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_radius_that_is_negative_or_not_finite_throws(double radiusMeters)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => GeoBox.Around(45, 15, radiusMeters));
        Assert.Equal("radiusMeters", error.ParamName);
    }

    [Fact]
    public void The_far_side_of_a_box_cut_at_the_east_edge_is_the_worked_example_range()
    {
        var far = GeoBox.FarSide(0, 179.9, 50_000);

        Assert.True(far.HasValue);
        Assert.Equal(-180.0, far.Value.MinLon);
        Assert.InRange(far.Value.MaxLon, -179.6504, -179.6502);
        Assert.True(far.Value.CrossesAntimeridian);
    }

    [Fact]
    public void The_far_side_of_a_box_cut_at_the_west_edge_runs_to_180()
    {
        var far = GeoBox.FarSide(0, -179.9, 50_000);

        Assert.True(far.HasValue);
        Assert.InRange(far.Value.MinLon, 179.6502, 179.6504);
        Assert.Equal(180.0, far.Value.MaxLon);
        Assert.True(far.Value.CrossesAntimeridian);
    }

    [Theory]
    [InlineData(0, 179.9, 50_000)]
    [InlineData(0, -179.9, 50_000)]
    [InlineData(60, 179.5, 500_000)]
    public void The_far_side_has_the_same_latitude_edges_as_the_near_box(double latitude, double longitude, double radiusMeters)
    {
        var near = GeoBox.Around(latitude, longitude, radiusMeters);
        var far = GeoBox.FarSide(latitude, longitude, radiusMeters);

        Assert.True(far.HasValue);
        Assert.Equal(near.MinLat, far.Value.MinLat);
        Assert.Equal(near.MaxLat, far.Value.MaxLat);
    }

    [Fact]
    public void A_box_not_cut_at_the_antimeridian_has_no_far_side()
    {
        Assert.Null(GeoBox.FarSide(45, 15, 1000));
    }

    [Fact]
    public void A_circle_that_reaches_a_pole_has_no_far_side()
    {
        Assert.Null(GeoBox.FarSide(89.99, 179.9, 100_000));
        Assert.Null(GeoBox.FarSide(-89.5, 10, 100_000));
    }

    [Fact]
    public void A_position_past_the_line_is_inside_the_far_side()
    {
        var far = GeoBox.FarSide(0, 179.9, 50_000);
        Assert.True(far.HasValue);

        // 40 km east of the centre is past the 180th meridian. Its longitude, written in -180 to 180, lies in the far box.
        var (_, lon) = Destination(0, 179.9, 90, 40_000);
        var wrapped = lon > 180 ? lon - 360 : lon;

        Assert.InRange(wrapped, far.Value.MinLon, far.Value.MaxLon);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(double.NaN, 15.0)]
    [InlineData(91.0, 15.0)]
    [InlineData(45.0, 181.0)]
    public void FarSide_refuses_an_unusable_centre_as_Around_does(double latitude, double longitude)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => GeoBox.FarSide(latitude, longitude, 100));
        Assert.Equal("latitude", error.ParamName);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FarSide_refuses_a_radius_that_is_negative_or_not_finite_as_Around_does(double radiusMeters)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => GeoBox.FarSide(45, 15, radiusMeters));
        Assert.Equal("radiusMeters", error.ParamName);
    }

    /// <summary>The point at a bearing and a distance from a start, on the same sphere the library uses.</summary>
    private static (double Lat, double Lon) Destination(double latitude, double longitude, double bearingDegrees, double distanceMeters)
    {
        var delta = distanceMeters / GeoDistance.EarthRadiusMeters;
        var theta = bearingDegrees * Math.PI / 180;
        var phi1 = latitude * Math.PI / 180;
        var lambda1 = longitude * Math.PI / 180;
        var phi2 = Math.Asin(Math.Sin(phi1) * Math.Cos(delta) + Math.Cos(phi1) * Math.Sin(delta) * Math.Cos(theta));
        var lambda2 = lambda1 + Math.Atan2(Math.Sin(theta) * Math.Sin(delta) * Math.Cos(phi1), Math.Cos(delta) - Math.Sin(phi1) * Math.Sin(phi2));
        return (phi2 * 180 / Math.PI, lambda2 * 180 / Math.PI);
    }
}
