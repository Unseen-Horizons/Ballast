// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Geo.Tests;

public class GeoDistanceTests
{
    [Fact]
    public void Antipodal_points_give_half_the_circumference_not_NaN()
    {
        // (51.5, 10) and (-51.5, -170) are on opposite sides of the Earth.
        var metres = GeoDistance.HaversineMeters(51.5, 10, -51.5, -170);

        Assert.False(double.IsNaN(metres));
        Assert.InRange(metres, 20_015_085.0, 20_015_088.0);
    }

    [Fact]
    public void Two_points_on_the_equator_half_a_turn_apart_give_pi_times_the_radius()
    {
        var metres = GeoDistance.HaversineMeters(0, 0, 0, 180);

        // pi x 6,371,000 m = 20,015,086.8 m
        Assert.InRange(metres, 20_015_086.0, 20_015_087.5);
    }

    [Fact]
    public void One_degree_along_the_equator_is_one_degree_of_latitude()
    {
        var metres = GeoDistance.HaversineMeters(0, 0, 0, 1);

        Assert.InRange(metres, GeoDistance.MetersPerDegreeLatitude - 0.01, GeoDistance.MetersPerDegreeLatitude + 0.01);
    }

    [Fact]
    public void One_degree_of_latitude_along_a_meridian_is_the_degree_length()
    {
        var metres = GeoDistance.HaversineMeters(45, 15, 46, 15);

        Assert.InRange(metres, GeoDistance.MetersPerDegreeLatitude - 0.01, GeoDistance.MetersPerDegreeLatitude + 0.01);
    }

    [Fact]
    public void The_distance_from_a_point_to_itself_is_zero()
    {
        Assert.Equal(0.0, GeoDistance.HaversineMeters(45.8131, 15.9619, 45.8131, 15.9619));
    }

    [Fact]
    public void The_distance_is_the_same_in_both_directions()
    {
        var forward = GeoDistance.HaversineMeters(45.8131, 15.9619, 43.5081, 16.4402);
        var back = GeoDistance.HaversineMeters(43.5081, 16.4402, 45.8131, 15.9619);

        Assert.InRange(back, forward - 1e-6, forward + 1e-6);
    }

    [Theory]
    [InlineData(double.NaN, 0.0, 0.0, 0.0)]
    [InlineData(0.0, 0.0, 91.0, 0.0)]
    [InlineData(0.0, 181.0, 0.0, 0.0)]
    [InlineData(0.0, 0.0, double.PositiveInfinity, 0.0)]
    public void A_coordinate_that_is_not_a_number_in_range_throws(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeoDistance.HaversineMeters(latitude1, longitude1, latitude2, longitude2));
    }

    [Fact]
    public void The_earth_radius_is_the_mean_radius_in_metres()
    {
        Assert.Equal(6_371_000.0, GeoDistance.EarthRadiusMeters);
    }

    [Fact]
    public void One_degree_of_latitude_is_about_111195_metres()
    {
        Assert.Equal(111194.93, Math.Round(GeoDistance.MetersPerDegreeLatitude, 2));
    }

    [Fact]
    public void One_degree_of_longitude_on_the_equator_is_one_degree_of_latitude()
    {
        Assert.Equal(GeoDistance.MetersPerDegreeLatitude, GeoDistance.MetersPerDegreeLongitude(0));
    }

    [Fact]
    public void One_degree_of_longitude_at_60_degrees_is_half_as_long()
    {
        Assert.InRange(GeoDistance.MetersPerDegreeLongitude(60), GeoDistance.MetersPerDegreeLatitude / 2 - 0.01, GeoDistance.MetersPerDegreeLatitude / 2 + 0.01);
    }

    [Fact]
    public void One_degree_of_longitude_at_the_pole_is_almost_zero()
    {
        Assert.InRange(GeoDistance.MetersPerDegreeLongitude(90), 0, 1e-6);
    }

    [Fact]
    public void One_degree_of_longitude_is_the_same_north_and_south()
    {
        Assert.InRange(GeoDistance.MetersPerDegreeLongitude(-45), GeoDistance.MetersPerDegreeLongitude(45) - 1e-6, GeoDistance.MetersPerDegreeLongitude(45) + 1e-6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(91.0)]
    [InlineData(-90.0001)]
    [InlineData(double.PositiveInfinity)]
    public void A_latitude_that_is_not_in_range_throws_for_a_longitude_degree(double latitude)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => GeoDistance.MetersPerDegreeLongitude(latitude));
        Assert.Equal("latitude", error.ParamName);
    }
}
