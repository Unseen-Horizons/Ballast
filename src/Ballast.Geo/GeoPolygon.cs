// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Ballast.Geo;

/// <summary>
/// Polygons written as rings of positions, and the test for whether a position is inside one.
/// </summary>
/// <remarks>
/// <para>
/// A ring is a list of positions written <c>[longitude, latitude]</c>, the order GeoJSON uses. The order is the one
/// thing to check when a ring is typed by hand. <see cref="Contains"/> takes latitude first, then longitude.
/// </para>
/// <para>
/// A polygon is one or more rings. The usual layout is a first ring for the outer edge and later rings for holes, and an
/// island inside a hole is a further ring. The test is even-odd over all the rings together, so the order of the rings does
/// not change the answer.
/// </para>
/// </remarks>
public static class GeoPolygon
{
    /// <summary>
    /// Reads a polygon from JSON: one ring, or a list of rings, of <c>[longitude, latitude]</c> positions.
    /// </summary>
    /// <param name="geoJson">
    /// The JSON text. A single ring is <c>[[lon,lat],…]</c>. Several rings are <c>[[[lon,lat],…],…]</c>. This is the coordinate
    /// layout of GeoJSON, without the wrapping object.
    /// </param>
    /// <returns>
    /// The rings, each an array of <c>[longitude, latitude]</c> positions, or <c>null</c> when the text is not one of those
    /// shapes. A ring needs at least three positions. A position needs two numbers that fit a <see cref="double"/>, and a
    /// third number, if present (an altitude, say), is ignored. Null for a blank or malformed text. The method never throws.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Vertex counts are not limited here, and coordinates are not range-checked: a longitude of 500 is read as given. A caller
    /// that caps the size of a ring applies its own cap to the result.
    /// </para>
    /// </remarks>
    public static double[][][]? TryParseRings(string? geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(geoJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;

            var first = root[0];
            if (first.ValueKind != JsonValueKind.Array || first.GetArrayLength() == 0) return null;

            // [[lon,lat],…]: the first element is a position, so the whole value is one ring.
            if (first[0].ValueKind == JsonValueKind.Number)
            {
                var single = ParseRing(root);
                return single is null ? null : [single];
            }

            // [[[lon,lat],…],…]: each element is a ring.
            List<double[][]> rings = [];
            foreach (var element in root.EnumerateArray())
            {
                var ring = ParseRing(element);
                if (ring is null) return null;
                rings.Add(ring);
            }
            return [.. rings];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a position is inside a polygon made of one or more rings.
    /// </summary>
    /// <param name="latitude">The position's latitude, from -90 to 90.</param>
    /// <param name="longitude">The position's longitude, from -180 to 180.</param>
    /// <param name="rings">
    /// The rings of the polygon, each an array of <c>[longitude, latitude]</c> positions, as <see cref="TryParseRings"/> returns
    /// them. An empty array encloses nothing, so the answer is <c>false</c>.
    /// </param>
    /// <returns><c>true</c> when the position is inside the polygon; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// <para>
    /// The rule is even-odd over all the rings together: a point is inside when a ray from it crosses the rings' edges an odd
    /// number of times. A point in a hole is outside, and a point in an island inside that hole is inside again.
    /// </para>
    /// <para>
    /// A point exactly on an edge or a vertex can come out either way. The answer there is not defined.
    /// </para>
    /// <para>
    /// A ring may repeat its first position at the end, as GeoJSON writes it. The repeat adds no edge and changes nothing.
    /// Rings are plane shapes in longitude and latitude: a ring that crosses the 180th meridian is not supported, and the
    /// zone has to be split at the line.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="rings"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The position is NaN, infinite, or out of range.</exception>
    /// <exception cref="ArgumentException">
    /// A ring has fewer than three positions, or a position is null, has fewer than two values, or has a value that is NaN or
    /// infinite. Nothing is skipped silently.
    /// </exception>
    public static bool Contains(double latitude, double longitude, double[][][] rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        GeoBounds.RequireLatitude(latitude, nameof(latitude));
        GeoBounds.RequireLongitude(longitude, nameof(longitude));

        var inside = false;
        for (var index = 0; index < rings.Length; index++)
        {
            var ring = rings[index];
            RequireRing(ring, index, nameof(rings));

            // Each ring's crossings flip the answer, so the count over all rings decides, as one even-odd test.
            if (RayCast(longitude, latitude, ring)) inside = !inside;
        }
        return inside;
    }

    /// <summary>One ring from a JSON array of positions, or null when it has fewer than three positions or a position is not two numbers.</summary>
    private static double[][]? ParseRing(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 3) return null;
        List<double[]> points = [];
        foreach (var point in element.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2) return null;

            // A value that is not a JSON number is refused here, because TryGetDouble throws on a string, null or array.
            if (point[0].ValueKind != JsonValueKind.Number || point[1].ValueKind != JsonValueKind.Number) return null;

            // TryGetDouble refuses a value that does not fit a double (1e400), which Parse would otherwise report as an exception.
            if (!point[0].TryGetDouble(out var longitude) || !point[1].TryGetDouble(out var latitude)) return null;
            if (!double.IsFinite(longitude) || !double.IsFinite(latitude)) return null;
            points.Add([longitude, latitude]);
        }
        return [.. points];
    }

    private static void RequireRing(double[][] ring, int index, string paramName)
    {
        if (ring is null || ring.Length < 3)
            throw new ArgumentException($"Ring {index} needs at least three positions.", paramName);

        foreach (var point in ring)
        {
            if (point is null || point.Length < 2 || !double.IsFinite(point[0]) || !double.IsFinite(point[1]))
                throw new ArgumentException($"A position in ring {index} must be two finite numbers, longitude then latitude.", paramName);
        }
    }

    /// <summary>Whether a ray from the point crosses the ring an odd number of times.</summary>
    private static bool RayCast(double x, double y, double[][] ring)
    {
        var inside = false;
        for (var i = 0; i < ring.Length; i++)
        {
            var j = i == 0 ? ring.Length - 1 : i - 1;
            var xi = ring[i][0];
            var yi = ring[i][1];
            var xj = ring[j][0];
            var yj = ring[j][1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}
