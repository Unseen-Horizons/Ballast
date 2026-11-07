// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time;

/// <summary>
/// The public holidays and days off of one country, or of one region of a country, for a range of years.
/// </summary>
/// <remarks>
/// A calendar lists only what its <see cref="Scope"/> says it lists. A holiday that a region, a city or a religious
/// community keeps on its own, and that is not in the list, is absent from it. A calendar never guesses a year it does
/// not cover: a year outside <see cref="FirstYear"/> to <see cref="LastYear"/> throws.
/// </remarks>
public interface IPublicHolidayCalendar
{
    /// <summary>
    /// The ISO 3166-1 alpha-2 code of the country, in upper case (for example <c>HR</c>).
    /// </summary>
    string CountryIso2 { get; }

    /// <summary>
    /// The region code the calendar is for, or <see langword="null"/> when it is the country's own calendar. The codes
    /// are the ones the registry lists for the country.
    /// </summary>
    string? Region { get; }

    /// <summary>
    /// One sentence saying what the list holds and what it leaves out. Show it wherever the list is used.
    /// </summary>
    string Scope { get; }

    /// <summary>
    /// The first year the calendar covers.
    /// </summary>
    int FirstYear { get; }

    /// <summary>
    /// The last year the calendar covers.
    /// </summary>
    int LastYear { get; }

    /// <summary>
    /// Whether the date is a public holiday or a day off in this calendar. A day in lieu counts.
    /// </summary>
    /// <param name="day">A calendar date in the country's own calendar. It has no zone.</param>
    /// <returns><see langword="true"/> when the date is listed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year of <paramref name="day"/> is outside the calendar's range.</exception>
    bool IsHoliday(DateOnly day);

    /// <summary>
    /// Every holiday and day off of a year, sorted by date. A day in lieu is its own entry with
    /// <see cref="PublicHoliday.IsSubstituteDay"/> set. Two holidays may fall on the same date (the Western and Orthodox
    /// Easter can coincide); both are listed.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <returns>The year's entries. The list is never empty for a covered year.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside the calendar's range.</exception>
    IReadOnlyList<PublicHoliday> InYear(int year);
}
