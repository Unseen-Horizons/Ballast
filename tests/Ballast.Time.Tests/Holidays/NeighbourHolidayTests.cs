// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Time.Tests;

/// <summary>
/// The Italian, Czech, Slovak and Hungarian calendars: the full 2026 list, the Easter-based dates for other years, the
/// 2100 pins and the year range. The Slovak table is not confirmed against its law (see Slovakia.cs), so its tests pin
/// the table as written and do not vouch for the law.
/// </summary>
public class NeighbourHolidayTests
{
    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<DateOnly> Dates(RuleBasedCalendar calendar, int year) =>
        calendar.InYear(year).Select(h => h.Date).ToList();

    private static void AssertShape(RuleBasedCalendar calendar, string iso2)
    {
        Assert.Equal(iso2, calendar.CountryIso2);
        Assert.Null(calendar.Region);
        Assert.False(string.IsNullOrWhiteSpace(calendar.Scope));
        Assert.Equal(2020, calendar.FirstYear);
        Assert.Equal(2100, calendar.LastYear);
    }

    private static void AssertOutsideRangeIsRefused(RuleBasedCalendar calendar)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.InYear(2019));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.InYear(2101));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.IsHoliday(D("2019-12-31")));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.IsHoliday(D("2101-01-01")));
    }

    private static void AssertSortedUniqueAndNoSubstitutes(RuleBasedCalendar calendar)
    {
        for (var year = calendar.FirstYear; year <= calendar.LastYear; year++)
        {
            // Two holidays may share a date when their names differ, so the check is on (date, name) pairs.
            var pairs = calendar.InYear(year).Select(h => (h.Date, h.EnglishName)).ToList();
            Assert.Equal(pairs.OrderBy(p => p.Date).ToList(), pairs);
            Assert.Equal(pairs.Count, pairs.Distinct().Count());
            Assert.All(calendar.InYear(year), h => Assert.False(h.IsSubstituteDay));
        }
    }

    // -- Italy (national days only; 4 October is not in the calendar) ------------------------------------

    [Fact]
    public void Italy_has_its_shape_and_refuses_years_outside_the_range()
    {
        AssertShape(ItHolidays.Calendar, "IT");
        AssertOutsideRangeIsRefused(ItHolidays.Calendar);
        AssertSortedUniqueAndNoSubstitutes(ItHolidays.Calendar);
    }

    [Fact]
    public void Italy_2026_lists_exactly_its_twelve_national_days()
    {
        Assert.Equal(
            new[]
            {
                D("2026-01-01"), D("2026-01-06"), D("2026-04-05"), D("2026-04-06"), D("2026-04-25"), D("2026-05-01"),
                D("2026-06-02"), D("2026-08-15"), D("2026-11-01"), D("2026-12-08"), D("2026-12-25"), D("2026-12-26"),
            },
            Dates(ItHolidays.Calendar, 2026));
    }

    [Fact]
    public void Italy_easter_monday_follows_easter_in_2025_2027_and_2100()
    {
        Assert.True(ItHolidays.Calendar.IsHoliday(D("2025-04-20")));
        Assert.True(ItHolidays.Calendar.IsHoliday(D("2025-04-21")));
        Assert.True(ItHolidays.Calendar.IsHoliday(D("2027-03-28")));
        Assert.True(ItHolidays.Calendar.IsHoliday(D("2027-03-29")));
        Assert.True(ItHolidays.Calendar.IsHoliday(D("2100-03-28")));
        Assert.True(ItHolidays.Calendar.IsHoliday(D("2100-03-29")));
    }

    [Fact]
    public void Italy_does_not_list_4_October_and_does_not_list_the_day_after_2_June()
    {
        Assert.False(ItHolidays.Calendar.IsHoliday(D("2026-10-04")));
        Assert.False(ItHolidays.Calendar.IsHoliday(D("2026-06-03")));
        Assert.True(ItHolidays.Calendar.IsHoliday(D("2026-06-02")));
    }

    // -- Czechia (Good Friday and 24 December are days of rest since the Act's amendments) -----------------

    [Fact]
    public void Czechia_has_its_shape_and_refuses_years_outside_the_range()
    {
        AssertShape(CzHolidays.Calendar, "CZ");
        AssertOutsideRangeIsRefused(CzHolidays.Calendar);
        AssertSortedUniqueAndNoSubstitutes(CzHolidays.Calendar);
    }

    [Fact]
    public void Czechia_2026_lists_exactly_its_thirteen_holidays()
    {
        Assert.Equal(
            new[]
            {
                D("2026-01-01"), D("2026-04-03"), D("2026-04-06"), D("2026-05-01"), D("2026-05-08"), D("2026-07-05"),
                D("2026-07-06"), D("2026-09-28"), D("2026-10-28"), D("2026-11-17"), D("2026-12-24"), D("2026-12-25"),
                D("2026-12-26"),
            },
            Dates(CzHolidays.Calendar, 2026));
    }

    [Fact]
    public void Czechia_good_friday_and_easter_monday_follow_easter_in_2020_2025_2027_and_2100()
    {
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2020-04-10")));
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2020-04-13")));
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2025-04-18")));
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2025-04-21")));
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2027-03-26")));
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2027-03-29")));
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2100-03-26")));
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2100-03-29")));
    }

    [Fact]
    public void Czechia_17_November_carries_the_full_name_of_the_Act()
    {
        var holiday = CzHolidays.Calendar.InYear(2026).Single(h => h.Date == D("2026-11-17"));
        Assert.Equal("Den boje za svobodu a demokracii a Mezinárodní den studentstva", holiday.LocalName);
    }

    [Fact]
    public void Czechia_christmas_eve_is_listed_and_the_day_after_the_last_holiday_is_not()
    {
        Assert.True(CzHolidays.Calendar.IsHoliday(D("2026-12-24")));
        Assert.False(CzHolidays.Calendar.IsHoliday(D("2026-12-27")));
    }

    // -- Slovakia (table NOT CONFIRMED against the Act; pins the table as written) ------------------------

    [Fact]
    public void Slovakia_table_has_its_shape_and_refuses_years_outside_the_range()
    {
        AssertShape(SkHolidays.Calendar, "SK");
        AssertOutsideRangeIsRefused(SkHolidays.Calendar);
        AssertSortedUniqueAndNoSubstitutes(SkHolidays.Calendar);
    }

    [Fact]
    public void Slovakia_table_2026_lists_its_fifteen_rows()
    {
        Assert.Equal(
            new[]
            {
                D("2026-01-01"), D("2026-01-06"), D("2026-04-03"), D("2026-04-06"), D("2026-05-01"), D("2026-05-08"),
                D("2026-07-05"), D("2026-08-29"), D("2026-09-01"), D("2026-09-15"), D("2026-11-01"), D("2026-11-17"),
                D("2026-12-24"), D("2026-12-25"), D("2026-12-26"),
            },
            Dates(SkHolidays.Calendar, 2026));
    }

    [Fact]
    public void Slovakia_good_friday_and_easter_monday_follow_easter_in_2025_2027_and_2100()
    {
        Assert.True(SkHolidays.Calendar.IsHoliday(D("2025-04-18")));
        Assert.True(SkHolidays.Calendar.IsHoliday(D("2025-04-21")));
        Assert.True(SkHolidays.Calendar.IsHoliday(D("2027-03-26")));
        Assert.True(SkHolidays.Calendar.IsHoliday(D("2027-03-29")));
        Assert.True(SkHolidays.Calendar.IsHoliday(D("2100-03-26")));
        Assert.True(SkHolidays.Calendar.IsHoliday(D("2100-03-29")));
    }

    // -- Hungary (section 102 (1); Easter Sunday and Whit Sunday are not in it) -------------------------

    [Fact]
    public void Hungary_has_its_shape_and_refuses_years_outside_the_range()
    {
        AssertShape(HuHolidays.Calendar, "HU");
        AssertOutsideRangeIsRefused(HuHolidays.Calendar);
        AssertSortedUniqueAndNoSubstitutes(HuHolidays.Calendar);
    }

    [Fact]
    public void Hungary_2026_lists_exactly_its_eleven_public_holidays()
    {
        Assert.Equal(
            new[]
            {
                D("2026-01-01"), D("2026-03-15"), D("2026-04-03"), D("2026-04-06"), D("2026-05-01"), D("2026-05-25"),
                D("2026-08-20"), D("2026-10-23"), D("2026-11-01"), D("2026-12-25"), D("2026-12-26"),
            },
            Dates(HuHolidays.Calendar, 2026));
    }

    [Fact]
    public void Hungary_does_not_list_easter_sunday_or_whit_sunday()
    {
        Assert.False(HuHolidays.Calendar.IsHoliday(D("2026-04-05")));
        Assert.False(HuHolidays.Calendar.IsHoliday(D("2026-05-24")));
    }

    [Fact]
    public void Hungary_whit_monday_and_good_friday_follow_easter_in_2020_2027_and_2100()
    {
        Assert.True(HuHolidays.Calendar.IsHoliday(D("2020-06-01")));
        Assert.True(HuHolidays.Calendar.IsHoliday(D("2027-05-17")));
        Assert.True(HuHolidays.Calendar.IsHoliday(D("2100-03-26")));
        Assert.True(HuHolidays.Calendar.IsHoliday(D("2100-05-17")));
    }

}
