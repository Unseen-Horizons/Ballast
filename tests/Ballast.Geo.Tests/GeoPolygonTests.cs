// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Geo.Tests;

/// <summary>Ring reading and point-in-polygon. The first cases are those of the zone reader the library was ported from.</summary>
public class GeoPolygonTests
{
    // Lon/lat squares: the outer ring is 0..10 both ways, a hole is 4..6 both ways, and an island inside the hole is 4.5..5.5.
    private const string OuterRing = "[[0,0],[10,0],[10,10],[0,10],[0,0]]";
    private const string HoleRing = "[[4,4],[6,4],[6,6],[4,6],[4,4]]";
    private const string IslandRing = "[[4.5,4.5],[5.5,4.5],[5.5,5.5],[4.5,5.5],[4.5,4.5]]";

    [Fact]
    public void A_single_ring_is_read_as_one_ring()
    {
        var rings = GeoPolygon.TryParseRings(OuterRing);

        var ring = Assert.Single(rings!);
        Assert.Equal(5, ring.Length);
        Assert.Equal([10.0, 0.0], ring[1]);
    }

    [Fact]
    public void A_list_of_rings_is_read_as_each_ring()
    {
        var rings = GeoPolygon.TryParseRings($"[{OuterRing},{HoleRing}]");

        Assert.Equal(2, rings!.Length);
        Assert.Equal(5, rings[1].Length);
    }

    [Fact]
    public void Malformed_polygons_parse_to_null()
    {
        Assert.Null(GeoPolygon.TryParseRings(null));
        Assert.Null(GeoPolygon.TryParseRings(string.Empty));
        Assert.Null(GeoPolygon.TryParseRings("not json"));
        Assert.Null(GeoPolygon.TryParseRings("[]"));
        Assert.Null(GeoPolygon.TryParseRings("[[1,2],[3,4]]"));                 // a ring of two positions
        Assert.Null(GeoPolygon.TryParseRings("[[[1,2],[3,4]]]"));               // one ring of two positions, as a list of rings
        Assert.Null(GeoPolygon.TryParseRings("[[\"a\",\"b\"],[1,2],[3,4]]"));   // a position that is not two numbers
        Assert.Null(GeoPolygon.TryParseRings("{\"ring\":1}"));
    }

    [Theory]
    [InlineData("[[1,2],[\"x\",3],[4,5]]")]                                      // a string in a later position of a one-ring list
    [InlineData("[[1,2],[3,null],[4,5]]")]                                       // a null value in a later position
    [InlineData("[[1,2],[\"a\",\"b\"],[3,4]]")]                                  // a position that is not two numbers, after a number
    [InlineData("[[[[1,2],[3,4],[5,6]],[[1,2],[3,4],[5,6]],[[1,2],[3,4],[5,6]]]]")] // four levels deep, as a MultiPolygon's coordinates
    public void A_bad_value_in_a_later_position_gives_null_instead_of_throwing(string geoJson)
    {
        Assert.Null(GeoPolygon.TryParseRings(geoJson));
    }

    [Fact]
    public void A_number_too_large_for_a_double_makes_the_polygon_malformed_instead_of_throwing()
    {
        Assert.Null(GeoPolygon.TryParseRings("[[1e400,1],[2,2],[3,3]]"));
        Assert.Null(GeoPolygon.TryParseRings("[[[1e400,1],[2,2],[3,3]]]"));
    }

    [Fact]
    public void A_position_with_a_third_number_reads_its_longitude_and_latitude()
    {
        var ring = Assert.Single(GeoPolygon.TryParseRings("[[1,2,99],[3,4,99],[5,6,99]]")!);

        Assert.Equal([1.0, 2.0], ring[0]);
        Assert.Equal([5.0, 6.0], ring[2]);
    }

    [Fact]
    public void A_hole_is_outside_and_an_island_in_the_hole_is_inside()
    {
        var rings = GeoPolygon.TryParseRings($"[{OuterRing},{HoleRing},{IslandRing}]")!;

        Assert.True(GeoPolygon.Contains(1, 1, rings), "in the outer ring, out of the hole");
        Assert.False(GeoPolygon.Contains(5, 4.2, rings), "inside the hole, outside the island");
        Assert.True(GeoPolygon.Contains(5, 5, rings), "inside the island in the hole");
        Assert.False(GeoPolygon.Contains(11, 11, rings), "outside everything");
    }

    [Fact]
    public void A_point_in_a_hole_is_outside_whatever_the_order_of_the_rings()
    {
        var rings = GeoPolygon.TryParseRings($"[{HoleRing},{OuterRing}]")!;

        Assert.False(GeoPolygon.Contains(5, 4.2, rings));
        Assert.True(GeoPolygon.Contains(1, 1, rings));
    }

    [Fact]
    public void A_ring_is_longitude_then_latitude()
    {
        // A tall zone: longitudes 0 to 1, latitudes 0 to 10.
        var rings = GeoPolygon.TryParseRings("[[0,0],[1,0],[1,10],[0,10]]")!;

        Assert.True(GeoPolygon.Contains(latitude: 5, longitude: 0.5, rings));
        Assert.False(GeoPolygon.Contains(latitude: 0.5, longitude: 5, rings));
    }

    [Fact]
    public void A_ring_that_repeats_its_first_position_gives_the_same_answers()
    {
        var open = GeoPolygon.TryParseRings("[[0,0],[10,0],[10,10],[0,10]]")!;
        var closed = GeoPolygon.TryParseRings(OuterRing)!;

        foreach (var (lat, lon) in new[] { (5.0, 5.0), (0.0, 0.0), (20.0, 5.0), (5.0, -1.0) })
        {
            Assert.Equal(GeoPolygon.Contains(lat, lon, closed), GeoPolygon.Contains(lat, lon, open));
        }
    }

    [Fact]
    public void Contains_refuses_null_rings()
    {
        Assert.Throws<ArgumentNullException>(() => GeoPolygon.Contains(5, 5, null!));
    }

    [Fact]
    public void Contains_with_no_rings_is_false()
    {
        Assert.False(GeoPolygon.Contains(5, 5, []));
    }

    [Theory]
    [InlineData(double.NaN, 5.0)]
    [InlineData(5.0, double.NaN)]
    [InlineData(91.0, 5.0)]
    [InlineData(5.0, 181.0)]
    public void A_position_that_is_not_a_number_in_range_throws(double latitude, double longitude)
    {
        var rings = GeoPolygon.TryParseRings(OuterRing)!;

        Assert.Throws<ArgumentOutOfRangeException>(() => GeoPolygon.Contains(latitude, longitude, rings));
    }

    [Fact]
    public void A_ring_with_two_positions_is_refused_not_skipped()
    {
        double[][][] rings = [[[0.0, 0.0], [1.0, 1.0]]];

        var error = Assert.Throws<ArgumentException>(() => GeoPolygon.Contains(0.5, 0.5, rings));
        Assert.Equal("rings", error.ParamName);
    }

    [Fact]
    public void A_ring_with_a_position_that_is_not_finite_is_refused()
    {
        double[][][] rings = [[[0.0, 0.0], [double.NaN, 1.0], [1.0, 1.0]]];

        Assert.Throws<ArgumentException>(() => GeoPolygon.Contains(0.5, 0.5, rings));
    }

    [Fact]
    public void A_position_with_one_number_is_refused()
    {
        double[][][] rings = [[[0.0, 0.0], [1.0], [1.0, 1.0]]];

        Assert.Throws<ArgumentException>(() => GeoPolygon.Contains(0.5, 0.5, rings));
    }

    [Fact]
    public void A_point_outside_every_ring_is_outside()
    {
        var rings = GeoPolygon.TryParseRings(OuterRing)!;

        Assert.False(GeoPolygon.Contains(-5, 5, rings));
        Assert.False(GeoPolygon.Contains(5, 15, rings));
    }
}
