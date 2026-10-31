// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Geo;

/// <summary>
/// Distances between two positions, and the length of one degree of latitude and longitude.
/// </summary>
/// <remarks>
/// <para>
/// Distances are great-circle distances on a sphere with the Earth's mean radius. A sphere is an approximation: a distance
/// on the real, slightly flattened Earth can differ from it by about half a percent. That is enough to rank stops or to
/// estimate a walk. It is not a survey.
/// </para>
/// <para>
/// The methods here check their coordinates and throw when one is not a number in range. <see cref="GeoBounds.IsUsable"/>
/// is the one check that answers with a bool instead. This class does not refuse (0, 0): a distance from (0, 0) is a valid
/// calculation.
/// </para>
/// </remarks>
public static class GeoDistance
{
    /// <summary>The Earth's mean radius in metres. Every distance and degree length in this library uses this one radius.</summary>
    public const double EarthRadiusMeters = 6_371_000.0;

    /// <summary>
    /// The length of one degree of latitude in metres: π × <see cref="EarthRadiusMeters"/> ÷ 180, about 111,194.9.
    /// </summary>
    /// <remarks>
    /// One degree of longitude is this length times the cosine of the latitude. See <see cref="MetersPerDegreeLongitude"/>.
    /// </remarks>
    public const double MetersPerDegreeLatitude = Math.PI * EarthRadiusMeters / 180.0;

    /// <summary>
    /// The great-circle distance in metres between two positions, by the haversine formula.
    /// </summary>
    /// <param name="latitude1">The first position's latitude, from -90 to 90.</param>
    /// <param name="longitude1">The first position's longitude, from -180 to 180.</param>
    /// <param name="latitude2">The second position's latitude, from -90 to 90.</param>
    /// <param name="longitude2">The second position's longitude, from -180 to 180.</param>
    /// <returns>
    /// The distance in metres, from 0 up to half the circumference (about 20,015,087 m). Two points on opposite sides of
    /// the Earth give that maximum, not NaN.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is NaN, infinite, or out of range.</exception>
    public static double HaversineMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        GeoBounds.RequireLatitude(latitude1, nameof(latitude1));
        GeoBounds.RequireLongitude(longitude1, nameof(longitude1));
        GeoBounds.RequireLatitude(latitude2, nameof(latitude2));
        GeoBounds.RequireLongitude(longitude2, nameof(longitude2));

        var dLat = ToRadians(latitude2 - latitude1);
        var dLon = ToRadians(longitude2 - longitude1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRadians(latitude1)) * Math.Cos(ToRadians(latitude2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        // Rounding can push a just past 1 for two points on opposite sides of the Earth. Held to [0, 1], the square root
        // is defined and the result is half the circumference, not NaN.
        a = Math.Clamp(a, 0.0, 1.0);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>
    /// The length in metres of one degree of longitude at a latitude: <see cref="MetersPerDegreeLatitude"/> × cos(latitude).
    /// </summary>
    /// <param name="latitude">The latitude, from -90 to 90.</param>
    /// <returns>
    /// About 111,194.9 at the equator, half that at 60°, and close to zero at the poles.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The latitude is NaN, infinite, or out of range.</exception>
    public static double MetersPerDegreeLongitude(double latitude)
    {
        GeoBounds.RequireLatitude(latitude, nameof(latitude));
        return MetersPerDegreeLatitude * Math.Cos(ToRadians(latitude));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
