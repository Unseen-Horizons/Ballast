// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Geo;

/// <summary>
/// A rectangle in latitude and longitude, used as the first filter of a "nearby" search.
/// </summary>
/// <param name="MinLat">The southern edge, in degrees, from -90 to 90.</param>
/// <param name="MinLon">The western edge, in degrees, from -180 to 180.</param>
/// <param name="MaxLat">The northern edge, in degrees, from -90 to 90.</param>
/// <param name="MaxLon">The eastern edge, in degrees, from -180 to 180.</param>
/// <param name="CrossesAntimeridian">
/// <c>true</c> when the box was cut at the 180th meridian, because the circle it was made from reaches past it. The part on the
/// other side is given by <see cref="FarSide"/>. See the remarks.
/// </param>
/// <remarks>
/// <para>
/// A box made by <see cref="Around"/> covers every position within its radius, and it can also cover positions outside
/// that circle, in its corners. A search filters the candidates it finds in the box with
/// <see cref="GeoDistance.HaversineMeters"/> to get the exact circle.
/// </para>
/// <para>
/// When <see cref="CrossesAntimeridian"/> is <c>true</c>, the box covers only the part on this side of the cut. The rest of
/// the circle lies on the other side of the 180th meridian, and a search that must not miss those positions also searches
/// <see cref="FarSide"/> with the same arguments. At most one side can be cut, so there is at most one far box.
/// </para>
/// <para>
/// The constructor takes the four edges as given and checks nothing. <see cref="Around"/> is the way to make a box from a
/// point and a radius.
/// </para>
/// </remarks>
public readonly record struct GeoBox(double MinLat, double MinLon, double MaxLat, double MaxLon, bool CrossesAntimeridian)
{
    /// <summary>
    /// The box that holds every position within a radius of a centre.
    /// </summary>
    /// <param name="latitude">The centre's latitude, from -90 to 90.</param>
    /// <param name="longitude">The centre's longitude, from -180 to 180.</param>
    /// <param name="radiusMeters">The radius in metres. Zero is allowed, and gives a box of one point.</param>
    /// <returns>
    /// The box. Its latitude is cut at -90 and 90. A circle that reaches a pole covers every longitude, so its box spans
    /// -180 to 180 and does not report a crossing: nothing was cut.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A circle that passes the 180th meridian gives a box cut there, with <see cref="CrossesAntimeridian"/> set to
    /// <c>true</c>. <see cref="FarSide"/> gives the part beyond the cut, for the same arguments.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="latitude"/> and <paramref name="longitude"/> are not a usable position (see
    /// <see cref="GeoBounds.IsUsable"/>, which refuses (0, 0)), or <paramref name="radiusMeters"/> is negative, NaN or infinite.
    /// </exception>
    public static GeoBox Around(double latitude, double longitude, double radiusMeters)
    {
        var (minLat, maxLat, west, east) = Edges(latitude, longitude, radiusMeters);

        // The box is held at the line, and the cut is reported.
        var crosses = west < -180 || east > 180;
        return new GeoBox(minLat, Math.Max(west, -180), maxLat, Math.Min(east, 180), crosses);
    }

    /// <summary>
    /// The part of the circle's box beyond the 180th meridian, for a circle that <see cref="Around"/> cuts there.
    /// </summary>
    /// <param name="latitude">The centre's latitude, from -90 to 90.</param>
    /// <param name="longitude">The centre's longitude, from -180 to 180.</param>
    /// <param name="radiusMeters">The radius in metres, the same as <see cref="Around"/> takes.</param>
    /// <returns>
    /// The far box, with <see cref="CrossesAntimeridian"/> set to <c>true</c>, or <c>null</c> when <see cref="Around"/> with the
    /// same arguments cuts nothing. Its latitude edges are <see cref="Around"/>'s. Its longitude runs from -180 to the far edge
    /// when the east edge was cut, and from the far edge to 180 when the west edge was cut.
    /// </returns>
    /// <remarks>
    /// <para>
    /// At most one side is cut. The reach of a circle is at most 90 degrees, so it cannot pass the 180th meridian on both sides,
    /// and there is at most one far box.
    /// </para>
    /// <para>
    /// For a cut box, search both: <see cref="Around"/> for the near side and this method for the far side. For example,
    /// <c>Around(0, 179.9, 50_000)</c> is cut at 180, and <c>FarSide(0, 179.9, 50_000)</c> gives the longitudes -180 to -179.6503.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The same as <see cref="Around"/>: the same checks, with the same parameter names.
    /// </exception>
    public static GeoBox? FarSide(double latitude, double longitude, double radiusMeters)
    {
        var (minLat, maxLat, west, east) = Edges(latitude, longitude, radiusMeters);
        if (east > 180) return new GeoBox(minLat, -180, maxLat, east - 360, true);
        if (west < -180) return new GeoBox(minLat, west + 360, maxLat, 180, true);
        return null;
    }

    /// <summary>
    /// The edges of the circle's box before any cut. Latitude is held to the poles. Longitude is the reach either side of the
    /// centre, which may pass -180 or 180. A circle that reaches a pole covers every longitude, so it spans -180 to 180.
    /// </summary>
    private static (double MinLat, double MaxLat, double West, double East) Edges(double latitude, double longitude, double radiusMeters)
    {
        if (!GeoBounds.IsUsable(latitude, longitude))
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "The centre must be a usable position: numbers in range, and not (0, 0).");
        if (!double.IsFinite(radiusMeters) || radiusMeters < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), radiusMeters, "The radius must be a finite number of metres, zero or more.");

        // The radius as an angle at the centre of the sphere, in degrees.
        var angularDegrees = radiusMeters / GeoDistance.EarthRadiusMeters * 180.0 / Math.PI;
        var minLat = latitude - angularDegrees;
        var maxLat = latitude + angularDegrees;

        // A circle that reaches a pole covers every longitude. Nothing is cut, so the box does not cross the antimeridian.
        if (minLat <= -90 || maxLat >= 90)
            return (Math.Max(minLat, -90), Math.Min(maxLat, 90), -180, 180);

        // How far a circle of this angular radius reaches in longitude at this latitude (spherical trigonometry):
        // sin(reach) = sin(radius) / cos(latitude). The ratio is held to [-1, 1]. Rounding at the edge can step past 1,
        // and the wider answer, 90 degrees, is the safe one for a search.
        var angularRadians = angularDegrees * Math.PI / 180.0;
        var ratio = Math.Sin(angularRadians) / Math.Cos(latitude * Math.PI / 180.0);
        var reachDegrees = Math.Asin(Math.Clamp(ratio, -1.0, 1.0)) * 180.0 / Math.PI;

        return (minLat, maxLat, longitude - reachDegrees, longitude + reachDegrees);
    }
}
