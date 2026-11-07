// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time;

/// <summary>
/// The date of Easter Sunday, in the Western (Gregorian) and the Orthodox church's reckoning. Holiday calendars give
/// their movable days as offsets from these dates.
/// </summary>
/// <remarks>
/// <para>
/// Both are computed, never looked up. Western Easter uses the anonymous Gregorian algorithm (Meeus, Jones and Butcher).
/// Orthodox Easter uses the Julian algorithm (Meeus) and then adds the difference between the two calendars for the
/// year, which is 13 days from 1900 to 2099 and 14 days in 2100.
/// </para>
/// <para>
/// Both are valid for years 1583 to 9999, the years the Gregorian calendar covers. A year outside that range throws
/// <see cref="ArgumentOutOfRangeException"/>; it is never answered with a guess.
/// </para>
/// </remarks>
public static class Easter
{
    private const int FirstYear = 1583;
    private const int LastYear = 9999;

    /// <summary>
    /// Western (Gregorian) Easter Sunday of a year.
    /// </summary>
    /// <param name="year">The year, from 1583 to 9999.</param>
    /// <returns>The date of Easter Sunday in the Gregorian calendar.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside 1583 to 9999.</exception>
    public static DateOnly Western(int year)
    {
        CheckYear(year);

        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = (h + l - 7 * m + 114) % 31 + 1;

        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// Orthodox Easter Sunday of a year, as a date in the Gregorian calendar (the calendar civil life uses in the
    /// countries that keep it).
    /// </summary>
    /// <param name="year">The year, from 1583 to 9999.</param>
    /// <returns>The date of Orthodox Easter Sunday in the Gregorian calendar.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside 1583 to 9999.</exception>
    public static DateOnly Orthodox(int year)
    {
        CheckYear(year);

        int a = year % 4;
        int b = year % 7;
        int c = year % 19;
        int d = (19 * c + 15) % 30;
        int e = (2 * a + 4 * b - d + 34) % 7;
        int julianMonth = (d + e + 114) / 31;
        int julianDay = (d + e + 114) % 31 + 1;

        // The Julian date has the same day numbers as a Gregorian date; adding the calendar difference moves it on.
        DateOnly julian = new(year, julianMonth, julianDay);
        return julian.AddDays(year / 100 - year / 400 - 2);
    }

    private static void CheckYear(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, FirstYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, LastYear);
    }
}
