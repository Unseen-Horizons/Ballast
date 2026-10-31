// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Geo;

/// <summary>
/// The check that decides whether a latitude and longitude pair is a position worth storing or measuring.
/// </summary>
/// <remarks>
/// <para>
/// Every comparison with NaN is false, so a range check written as "not below -90 and not above 90" accepts NaN.
/// Text parsers also read the words <c>NaN</c> and <c>Infinity</c>. The checks here are patterns that name the accepted
/// range, so NaN and the infinities fall outside it without a separate test.
/// </para>
/// <para>
/// (0, 0) is refused too. It is a legal coordinate in the Gulf of Guinea, but in data it is almost always a missing
/// value that defaulted to zero. Negative zero equals zero under IEEE 754, so it is refused by the same test.
/// </para>
/// </remarks>
public static class GeoBounds
{
    /// <summary>
    /// Whether a latitude and longitude pair is a finite number in range, and not (0, 0).
    /// </summary>
    /// <param name="latitude">Degrees north of the equator, from -90 to 90.</param>
    /// <param name="longitude">Degrees east of Greenwich, from -180 to 180.</param>
    /// <returns>
    /// <c>true</c> when the pair is a usable position; otherwise <c>false</c>. The method never throws.
    /// </returns>
    public static bool IsUsable(double latitude, double longitude) =>
        latitude is >= -90 and <= 90
        && longitude is >= -180 and <= 180
        && !(latitude == 0 && longitude == 0);

    /// <summary>Throws when a latitude is not a number from -90 to 90.</summary>
    internal static void RequireLatitude(double latitude, string paramName)
    {
        if (latitude is not (>= -90 and <= 90))
            throw new ArgumentOutOfRangeException(paramName, latitude, "A latitude is a number from -90 to 90.");
    }

    /// <summary>Throws when a longitude is not a number from -180 to 180.</summary>
    internal static void RequireLongitude(double longitude, string paramName)
    {
        if (longitude is not (>= -180 and <= 180))
            throw new ArgumentOutOfRangeException(paramName, longitude, "A longitude is a number from -180 to 180.");
    }
}
