// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time.Tests;

/// <summary>
/// Serbia (RS), Montenegro (ME) and North Macedonia (MK). Pins the day lists and the Sunday rule on dates whose weekdays were
/// checked, including the Easter-based days from the Easter table.
/// </summary>
public class BalkansEastHolidayTests
{
    private static readonly DateOnly[] SerbiaDates2026 =
    [
        new(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 7), new(2026, 2, 15), new(2026, 2, 16), new(2026, 2, 17),
        new(2026, 4, 10), new(2026, 4, 11), new(2026, 4, 12), new(2026, 4, 13), new(2026, 5, 1), new(2026, 5, 2),
        new(2026, 11, 11),
    ];

    private static readonly DateOnly[] SerbiaDates2025 =
    [
        new(2025, 1, 1), new(2025, 1, 2), new(2025, 1, 7), new(2025, 2, 15), new(2025, 2, 16), new(2025, 2, 17),
        new(2025, 4, 18), new(2025, 4, 19), new(2025, 4, 20), new(2025, 4, 21), new(2025, 5, 1), new(2025, 5, 2),
        new(2025, 11, 11),
    ];

    private static readonly DateOnly[] MacedoniaDates2026 =
    [
        new(2026, 1, 1), new(2026, 1, 7), new(2026, 4, 12), new(2026, 4, 13), new(2026, 5, 1), new(2026, 5, 24),
        new(2026, 5, 25), new(2026, 8, 2), new(2026, 8, 3), new(2026, 9, 8), new(2026, 10, 11), new(2026, 10, 12),
        new(2026, 10, 23), new(2026, 12, 8),
    ];

    private static readonly DateOnly[] MontenegroDates2026 =
    [
        new(2026, 1, 1), new(2026, 1, 2), new(2026, 5, 1), new(2026, 5, 2), new(2026, 5, 21), new(2026, 5, 22),
        new(2026, 7, 13), new(2026, 7, 14),
    ];

    // -- Serbia ----------------------------------------------------------------------------------------

    [Fact]
    public void Serbia_covers_2020_to_2100_and_no_region()
    {
        Assert.Equal("RS", RsHolidays.Calendar.CountryIso2);
        Assert.Null(RsHolidays.Calendar.Region);
        Assert.Equal(2020, RsHolidays.Calendar.FirstYear);
        Assert.Equal(2100, RsHolidays.Calendar.LastYear);
    }

    [Fact]
    public void Serbia_2026_lists_thirteen_days_in_order_with_the_day_in_lieu_of_sunday_15_february()
    {
        var dates = RsHolidays.Calendar.InYear(2026).Select(h => h.Date).ToArray();

        Assert.Equal<DateOnly>(SerbiaDates2026, dates);
    }

    [Fact]
    public void Serbia_2026_day_in_lieu_is_flagged_and_named_from_its_sunday_original()
    {
        var sub = RsHolidays.Calendar.InYear(2026).Single(h => h.IsSubstituteDay);

        Assert.Equal(new DateOnly(2026, 2, 17), sub.Date);
        Assert.Equal("Sretenje, Dan državnosti Srbije", sub.LocalName);
        Assert.Equal("Statehood Day (day off in lieu)", sub.EnglishName);
        Assert.False(RsHolidays.Calendar.InYear(2026).Single(h => h.Date == new DateOnly(2026, 2, 15)).IsSubstituteDay);
    }

    [Fact]
    public void Serbia_2025_moves_sunday_16_february_to_17_february()
    {
        var dates = RsHolidays.Calendar.InYear(2025);

        Assert.Equal<DateOnly>(SerbiaDates2025, dates.Select(h => h.Date).ToArray());
        Assert.Equal(new DateOnly(2025, 2, 17), dates.Single(h => h.IsSubstituteDay).Date);
    }

    [Fact]
    public void Serbia_2027_moves_sunday_2_may_past_orthodox_easter_monday_to_4_may()
    {
        var dates = RsHolidays.Calendar.InYear(2027);

        // 1 May 2027 is a Saturday, so it is not moved; 2 May is a Sunday and the next free working day is 4 May
        // (3 May is Orthodox Easter Monday).
        var sub = dates.Single(h => h.IsSubstituteDay);
        Assert.Equal(new DateOnly(2027, 5, 4), sub.Date);
        Assert.Contains(dates, h => h.Date == new DateOnly(2027, 4, 30) && h.EnglishName == "Orthodox Good Friday");
        Assert.Contains(dates, h => h.Date == new DateOnly(2027, 5, 3) && h.EnglishName == "Orthodox Easter Monday");
    }

    [Fact]
    public void Serbia_2100_orthodox_easter_on_2_may_gives_4_may_as_day_in_lieu()
    {
        Assert.True(RsHolidays.Calendar.IsHoliday(new DateOnly(2100, 4, 30)));
        Assert.True(RsHolidays.Calendar.IsHoliday(new DateOnly(2100, 5, 2)));
        Assert.True(RsHolidays.Calendar.IsHoliday(new DateOnly(2100, 5, 3)));
        Assert.True(RsHolidays.Calendar.IsHoliday(new DateOnly(2100, 5, 4)));
        Assert.Equal(new DateOnly(2100, 5, 4), RsHolidays.Calendar.InYear(2100).Single(h => h.IsSubstituteDay).Date);
    }

    [Fact]
    public void Serbia_victory_day_9_may_is_a_working_day_and_armistice_day_is_a_holiday()
    {
        Assert.False(RsHolidays.Calendar.IsHoliday(new DateOnly(2025, 5, 9)));
        Assert.True(RsHolidays.Calendar.IsHoliday(new DateOnly(2026, 11, 11)));
    }

    [Fact]
    public void Serbia_IsHoliday_is_true_on_a_holiday_and_false_the_day_before_or_after()
    {
        Assert.True(RsHolidays.Calendar.IsHoliday(new DateOnly(2026, 4, 10)));
        Assert.False(RsHolidays.Calendar.IsHoliday(new DateOnly(2026, 4, 9)));
        Assert.True(RsHolidays.Calendar.IsHoliday(new DateOnly(2026, 2, 17)));
        Assert.False(RsHolidays.Calendar.IsHoliday(new DateOnly(2026, 2, 18)));
    }

    [Fact]
    public void Serbia_years_outside_2020_to_2100_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RsHolidays.Calendar.InYear(2019));
        Assert.Throws<ArgumentOutOfRangeException>(() => RsHolidays.Calendar.InYear(2101));
        Assert.Throws<ArgumentOutOfRangeException>(() => RsHolidays.Calendar.IsHoliday(new DateOnly(2019, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RsHolidays.Calendar.IsHoliday(new DateOnly(2101, 1, 1)));
    }

    [Fact]
    public void Serbia_InYear_is_sorted_and_has_no_duplicate_holiday_in_any_year()
    {
        for (var year = 2020; year <= 2100; year++)
        {
            // Two holidays may share a date when their names differ, so the check is on (date, name) pairs.
            var pairs = RsHolidays.Calendar.InYear(year).Select(h => (h.Date, h.EnglishName)).ToArray();

            Assert.Equal(pairs.OrderBy(p => p.Date).ToArray(), pairs);
            Assert.True(pairs.Distinct().Count() == pairs.Length, $"Serbia {year} lists a holiday with the same date and name twice.");
        }
    }

    // -- Montenegro (NOT CONFIRMED, unregistered) ------------------------------------------------------

    [Fact]
    public void Montenegro_covers_2020_to_2100_and_no_region()
    {
        Assert.Equal("ME", MeHolidays.Calendar.CountryIso2);
        Assert.Null(MeHolidays.Calendar.Region);
        Assert.Equal(2020, MeHolidays.Calendar.FirstYear);
        Assert.Equal(2100, MeHolidays.Calendar.LastYear);
    }

    [Fact]
    public void Montenegro_2026_lists_both_days_of_each_two_day_holiday()
    {
        var dates = MeHolidays.Calendar.InYear(2026);

        Assert.Equal<DateOnly>(MontenegroDates2026, dates.Select(h => h.Date).ToArray());
        Assert.All(dates, h => Assert.False(h.IsSubstituteDay));
    }

    [Fact]
    public void Montenegro_does_not_list_orthodox_christmas_or_easter_as_the_law_text_read_does_not()
    {
        Assert.False(MeHolidays.Calendar.IsHoliday(new DateOnly(2026, 1, 7)));
        Assert.False(MeHolidays.Calendar.IsHoliday(new DateOnly(2026, 4, 12)));
        Assert.False(MeHolidays.Calendar.IsHoliday(new DateOnly(2026, 4, 13)));
    }

    [Fact]
    public void Montenegro_years_outside_2020_to_2100_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeHolidays.Calendar.InYear(2019));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeHolidays.Calendar.InYear(2101));
    }

    // -- North Macedonia (NOT CONFIRMED, unregistered) -------------------------------------------------

    [Fact]
    public void Macedonia_covers_2020_to_2100_and_no_region()
    {
        Assert.Equal("MK", MkHolidays.Calendar.CountryIso2);
        Assert.Null(MkHolidays.Calendar.Region);
        Assert.Equal(2020, MkHolidays.Calendar.FirstYear);
        Assert.Equal(2100, MkHolidays.Calendar.LastYear);
    }

    [Fact]
    public void Macedonia_2026_lists_fourteen_days_with_four_days_in_lieu_of_sunday_holidays()
    {
        var dates = MkHolidays.Calendar.InYear(2026);

        Assert.Equal<DateOnly>(MacedoniaDates2026, dates.Select(h => h.Date).ToArray());

        var subs = dates.Where(h => h.IsSubstituteDay).Select(h => h.Date).ToArray();
        var expectedSubs = new DateOnly[] { new(2026, 4, 13), new(2026, 5, 25), new(2026, 8, 3), new(2026, 10, 12) };
        Assert.Equal<DateOnly>(expectedSubs, subs);
    }

    [Fact]
    public void Macedonia_day_in_lieu_takes_the_sunday_holiday_name_with_the_suffix()
    {
        var sub = MkHolidays.Calendar.InYear(2026).Single(h => h.Date == new DateOnly(2026, 4, 13));

        Assert.True(sub.IsSubstituteDay);
        Assert.Equal("Прв ден Велигден", sub.LocalName);
        Assert.Equal("Orthodox Easter Sunday (day off in lieu)", sub.EnglishName);
    }

    [Fact]
    public void Macedonia_a_saturday_holiday_is_not_moved()
    {
        // 2 May 2026 is a Saturday and is not on the list, so Monday 4 May is not a day off.
        Assert.False(MkHolidays.Calendar.IsHoliday(new DateOnly(2026, 5, 2)));
        Assert.False(MkHolidays.Calendar.IsHoliday(new DateOnly(2026, 5, 4)));
    }

    [Fact]
    public void Macedonia_years_outside_2020_to_2100_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MkHolidays.Calendar.InYear(2019));
        Assert.Throws<ArgumentOutOfRangeException>(() => MkHolidays.Calendar.IsHoliday(new DateOnly(2101, 1, 1)));
    }

    [Fact]
    public void Macedonia_InYear_is_sorted_and_has_no_duplicate_holiday_in_any_year()
    {
        for (var year = 2020; year <= 2100; year++)
        {
            // Two holidays may share a date when their names differ, so the check is on (date, name) pairs.
            var pairs = MkHolidays.Calendar.InYear(year).Select(h => (h.Date, h.EnglishName)).ToArray();

            Assert.Equal(pairs.OrderBy(p => p.Date).ToArray(), pairs);
            Assert.True(pairs.Distinct().Count() == pairs.Length, $"Macedonia {year} lists a holiday with the same date and name twice.");
        }
    }
}
