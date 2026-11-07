// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time;

/// <summary>
/// How a holiday's date is found in a year.
/// </summary>
internal enum HolidayRuleKind
{
    /// <summary>A month and day that are the same in every year.</summary>
    Fixed,

    /// <summary>Western (Gregorian) Easter Sunday plus the rule's offset in days.</summary>
    WesternEaster,

    /// <summary>Orthodox Easter Sunday plus the rule's offset in days.</summary>
    OrthodoxEaster,
}

/// <summary>
/// One line of a country's holiday table. Build it with the factory methods, which check their arguments.
/// </summary>
/// <param name="Kind">How the date is found.</param>
/// <param name="Month">The month of a <see cref="HolidayRuleKind.Fixed"/> rule; 0 for the Easter kinds.</param>
/// <param name="Day">The day of a <see cref="HolidayRuleKind.Fixed"/> rule; 0 for the Easter kinds.</param>
/// <param name="Offset">The days from Easter Sunday for the Easter kinds; 0 for a fixed rule.</param>
/// <param name="LocalName">The name in the country's language.</param>
/// <param name="EnglishName">The English name.</param>
/// <param name="Substitutable">
/// True when a day in lieu is given if the holiday falls on a Sunday and the calendar moves Sunday holidays.
/// </param>
internal sealed record HolidayRule(
    HolidayRuleKind Kind,
    int Month,
    int Day,
    int Offset,
    string LocalName,
    string EnglishName,
    bool Substitutable)
{
    /// <summary>
    /// A holiday on the same month and day every year.
    /// </summary>
    /// <param name="month">The month, 1 to 12.</param>
    /// <param name="day">The day of the month. 29 February is refused: a rule on it would be missing in common years.</param>
    /// <param name="localName">The name in the country's language.</param>
    /// <param name="englishName">The English name.</param>
    /// <param name="substitutable">True when a Sunday holiday gets a day in lieu.</param>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The month or day is not a date in a common year.</exception>
    /// <exception cref="ArgumentException">A name is null, empty or white space.</exception>
    public static HolidayRule Fixed(int month, int day, string localName, string englishName, bool substitutable = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        int longestMonth = month == 2 ? 28 : DateTime.DaysInMonth(2001, month);
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, longestMonth);

        return Make(HolidayRuleKind.Fixed, month, day, 0, localName, englishName, substitutable);
    }

    /// <summary>
    /// A holiday a number of days from Western (Gregorian) Easter Sunday.
    /// </summary>
    /// <param name="offset">The days from Easter Sunday; negative for days before it.</param>
    /// <param name="localName">The name in the country's language.</param>
    /// <param name="englishName">The English name.</param>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentException">A name is null, empty or white space.</exception>
    public static HolidayRule WesternEaster(int offset, string localName, string englishName) =>
        Make(HolidayRuleKind.WesternEaster, 0, 0, offset, localName, englishName, false);

    /// <summary>
    /// A holiday a number of days from Orthodox Easter Sunday.
    /// </summary>
    /// <param name="offset">The days from Easter Sunday; negative for days before it.</param>
    /// <param name="localName">The name in the country's language.</param>
    /// <param name="englishName">The English name.</param>
    /// <param name="substitutable">True when a Sunday holiday gets a day in lieu.</param>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentException">A name is null, empty or white space.</exception>
    public static HolidayRule OrthodoxEaster(int offset, string localName, string englishName, bool substitutable = false) =>
        Make(HolidayRuleKind.OrthodoxEaster, 0, 0, offset, localName, englishName, substitutable);

    /// <summary>
    /// The date this rule gives in a year. The year is not range-checked here; the calendar checks it.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <returns>The date of the holiday.</returns>
    public DateOnly DateIn(int year) => Kind switch
    {
        HolidayRuleKind.Fixed => new DateOnly(year, Month, Day),
        HolidayRuleKind.WesternEaster => Easter.Western(year).AddDays(Offset),
        HolidayRuleKind.OrthodoxEaster => Easter.Orthodox(year).AddDays(Offset),
        _ => throw new InvalidOperationException($"Unknown holiday rule kind {Kind}."),
    };

    private static HolidayRule Make(
        HolidayRuleKind kind,
        int month,
        int day,
        int offset,
        string localName,
        string englishName,
        bool substitutable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);
        ArgumentException.ThrowIfNullOrWhiteSpace(englishName);
        return new HolidayRule(kind, month, day, offset, localName, englishName, substitutable);
    }
}
