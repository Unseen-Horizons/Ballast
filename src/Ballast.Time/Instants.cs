// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time;

/// <summary>
/// Turns a <see cref="DateTime"/> into a UTC instant, according to its <see cref="DateTimeKind"/>.
/// </summary>
/// <remarks>
/// <para>
/// A value from a wire format is one of three kinds, and each is read differently:
/// </para>
/// <list type="bullet">
/// <item><description><b>Utc</b>: already an instant. It is returned as it is.</description></item>
/// <item><description><b>Local</b>: the machine's own representation of an instant, made by .NET (a JSON reader makes one
/// from a time with an offset). It is converted back to UTC with <see cref="DateTime.ToUniversalTime"/>, which is its
/// exact inverse.</description></item>
/// <item><description><b>Unspecified</b>: read as UTC. That is right only where the field is documented as UTC (a name
/// ending in <c>Utc</c>, or a value with a <c>Z</c> suffix). A wall-clock time with a zone is not an instant: use
/// <see cref="PlaceTime.WallToUtc(DateTime, TimeZoneInfo)"/> for it.</description></item>
/// </list>
/// <para>
/// The library never creates a Local value. Every value it returns is Utc, Unspecified or a text.
/// </para>
/// </remarks>
public static class Instants
{
    /// <summary>
    /// The UTC instant that <paramref name="value"/> represents.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <returns>
    /// The value with <see cref="DateTimeKind.Utc"/> kind. A Utc value is returned unchanged, and an Unspecified value is
    /// marked as UTC without any change to its clock reading.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A Local value is the machine's representation of an instant, and <see cref="DateTime.ToUniversalTime"/> is the
    /// exact inverse of the conversion that made it. In the repeated hour of an autumn switch, the value cannot say which
    /// of its two instants it came from, so keep the UTC instant wherever it is at hand.
    /// </para>
    /// <para>
    /// .NET's own conversion can clamp a Local value at the very ends of <see cref="DateTime"/>'s range instead of failing.
    /// Only a value within about a day of either end can be affected.
    /// </para>
    /// </remarks>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
