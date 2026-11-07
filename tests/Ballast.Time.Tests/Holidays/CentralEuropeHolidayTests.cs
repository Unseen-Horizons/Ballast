// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Time.Tests;

public class CentralEuropeHolidayTests
{
    public static TheoryData<string> CountryCodes => new() { "HR", "SI", "AT", "DE" };

    [Theory]
    [MemberData(nameof(CountryCodes))]
    public void Every_calendar_covers_2020_to_2100(string code)
    {
        var calendar = Calendar(code);

        Assert.Equal(2020, calendar.FirstYear);
        Assert.Equal(2100, calendar.LastYear);
    }

    [Theory]
    [MemberData(nameof(CountryCodes))]
    public void Years_outside_the_range_throw(string code)
    {
        var calendar = Calendar(code);

        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.InYear(2019));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.InYear(2101));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.IsHoliday(new DateOnly(2019, 12, 31)));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.IsHoliday(new DateOnly(2101, 1, 1)));
    }

    [Theory]
    [MemberData(nameof(CountryCodes))]
    public void Every_year_is_sorted_with_no_duplicate_holiday(string code)
    {
        var calendar = Calendar(code);

        for (var year = 2020; year <= 2100; year++)
        {
            // Two holidays may share a date when their names differ, so the check is on (date, name) pairs.
            var pairs = calendar.InYear(year).Select(h => (h.Date, h.EnglishName)).ToArray();

            Assert.Equal(pairs.Distinct().OrderBy(p => p.Date).ToArray(), pairs);
        }
    }

    [Theory]
    [MemberData(nameof(CountryCodes))]
    public void No_substitute_days_are_flagged_for_these_countries(string code)
    {
        var calendar = Calendar(code);

        Assert.All(calendar.InYear(2026), h => Assert.False(h.IsSubstituteDay));
    }

    [Fact]
    public void Croatia_2026_has_its_14_days()
    {
        Assert.Equal(
            Dates(
                "2026-01-01", "2026-01-06", "2026-04-05", "2026-04-06", "2026-05-01", "2026-05-30", "2026-06-04",
                "2026-06-22", "2026-08-05", "2026-08-15", "2026-11-01", "2026-11-18", "2026-12-25", "2026-12-26"),
            DatesOf("HR", 2026));
    }

    [Fact]
    public void Slovenia_2026_has_its_15_days_and_no_whit_monday()
    {
        Assert.Equal(
            Dates(
                "2026-01-01", "2026-01-02", "2026-02-08", "2026-04-05", "2026-04-06", "2026-04-27", "2026-05-01",
                "2026-05-02", "2026-05-24", "2026-06-25", "2026-08-15", "2026-10-31", "2026-11-01",
                "2026-12-25", "2026-12-26"),
            DatesOf("SI", 2026));
    }

    [Fact]
    public void Austria_2026_has_its_13_days()
    {
        Assert.Equal(
            Dates(
                "2026-01-01", "2026-01-06", "2026-04-06", "2026-05-01", "2026-05-14", "2026-05-25", "2026-06-04",
                "2026-08-15", "2026-10-26", "2026-11-01", "2026-12-08", "2026-12-25", "2026-12-26"),
            DatesOf("AT", 2026));
    }

    [Fact]
    public void Germany_2026_has_its_9_nationwide_days()
    {
        Assert.Equal(
            Dates(
                "2026-01-01", "2026-04-03", "2026-04-06", "2026-05-01", "2026-05-14", "2026-05-25", "2026-10-03",
                "2026-12-25", "2026-12-26"),
            DatesOf("DE", 2026));
    }

    [Fact]
    public void Movable_days_in_2025_follow_the_easter_table()
    {
        // Western Easter 2025 is 20 April.
        Assert.Contains(Date("2025-04-20"), DatesOf("HR", 2025));
        Assert.Contains(Date("2025-04-21"), DatesOf("HR", 2025));
        Assert.Contains(Date("2025-06-19"), DatesOf("HR", 2025));
        Assert.Contains(Date("2025-06-08"), DatesOf("SI", 2025));
        Assert.DoesNotContain(Date("2025-06-09"), DatesOf("SI", 2025));
        Assert.Contains(Date("2025-05-29"), DatesOf("AT", 2025));
        Assert.Contains(Date("2025-06-09"), DatesOf("AT", 2025));
        Assert.Contains(Date("2025-04-18"), DatesOf("DE", 2025));
        Assert.Contains(Date("2025-05-29"), DatesOf("DE", 2025));
        Assert.Contains(Date("2025-06-09"), DatesOf("DE", 2025));
    }

    [Fact]
    public void Movable_days_in_2027_follow_the_easter_table()
    {
        // Western Easter 2027 is 28 March.
        Assert.Contains(Date("2027-03-28"), DatesOf("HR", 2027));
        Assert.Contains(Date("2027-03-29"), DatesOf("HR", 2027));
        Assert.Contains(Date("2027-05-27"), DatesOf("HR", 2027));
        Assert.Contains(Date("2027-05-16"), DatesOf("SI", 2027));
        Assert.DoesNotContain(Date("2027-05-17"), DatesOf("SI", 2027));
        Assert.Contains(Date("2027-05-06"), DatesOf("AT", 2027));
        Assert.Contains(Date("2027-05-27"), DatesOf("AT", 2027));
        Assert.Contains(Date("2027-03-26"), DatesOf("DE", 2027));
        Assert.Contains(Date("2027-05-17"), DatesOf("DE", 2027));
    }

    [Fact]
    public void Movable_days_in_2100_follow_the_easter_table()
    {
        // Western Easter 2100 is 28 March.
        Assert.Contains(Date("2100-03-28"), DatesOf("HR", 2100));
        Assert.Contains(Date("2100-05-27"), DatesOf("HR", 2100));
        Assert.Contains(Date("2100-05-16"), DatesOf("SI", 2100));
        Assert.Contains(Date("2100-05-06"), DatesOf("AT", 2100));
        Assert.Contains(Date("2100-03-26"), DatesOf("DE", 2100));
        Assert.Contains(Date("2100-05-17"), DatesOf("DE", 2100));
    }

    [Fact]
    public void A_holiday_is_true_and_the_day_after_is_false()
    {
        var hr = Calendar("HR");
        var de = Calendar("DE");

        Assert.True(hr.IsHoliday(new DateOnly(2026, 4, 6)));
        Assert.False(hr.IsHoliday(new DateOnly(2026, 4, 7)));
        Assert.True(de.IsHoliday(new DateOnly(2026, 10, 3)));
        Assert.False(de.IsHoliday(new DateOnly(2026, 10, 4)));
    }

    [Fact]
    public void German_unity_day_on_a_saturday_is_not_moved()
    {
        // 3 October 2026 is a Saturday. Germany has no moved days, so the Monday is not a holiday.
        var de = Calendar("DE");

        Assert.True(de.IsHoliday(new DateOnly(2026, 10, 3)));
        Assert.False(de.IsHoliday(new DateOnly(2026, 10, 5)));
    }

    private static RuleBasedCalendar Calendar(string code) => code switch
    {
        "HR" => HrHolidays.Calendar,
        "SI" => SiHolidays.Calendar,
        "AT" => AtHolidays.Calendar,
        "DE" => DeHolidays.Calendar,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Not a calendar in this test."),
    };

    private static DateOnly[] DatesOf(string code, int year) =>
        Calendar(code).InYear(year).Select(h => h.Date).ToArray();

    private static DateOnly[] Dates(params string[] isoDates) =>
        isoDates.Select(Date).ToArray();

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
