// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time.Tests;

public class BalkansWestHolidayTests
{
    [Fact]
    public void Every_level_covers_2020_to_2100()
    {
        foreach (var calendar in All())
        {
            Assert.Equal(2020, calendar.FirstYear);
            Assert.Equal(2100, calendar.LastYear);
        }
    }

    [Fact]
    public void Years_outside_the_range_throw()
    {
        foreach (var calendar in All())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = calendar.InYear(2019); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = calendar.InYear(2101); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = calendar.IsHoliday(new DateOnly(2019, 12, 31)); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = calendar.IsHoliday(new DateOnly(2101, 1, 1)); });
        }
    }

    [Fact]
    public void Every_year_list_is_sorted_by_date()
    {
        foreach (var calendar in All())
        {
            foreach (var year in new[] { 2020, 2025, 2026, 2027, 2100 })
            {
                AssertSorted(calendar.InYear(year));
            }
        }
    }

    [Fact]
    public void State_2026_has_the_two_proclamation_days()
    {
        var list = BaHolidays.State.InYear(2026);

        Assert.Equal(2, list.Count);
        Assert.Equal(new DateOnly(2026, 3, 1), list[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 25), list[1].Date);
        Assert.Null(BaHolidays.State.Region);
    }

    [Fact]
    public void State_is_not_moved_on_a_sunday()
    {
        // 1 March 2026 is a Sunday; the FBiH ministry said it is not moved in 2026.
        var list = BaHolidays.State.InYear(2026);

        Assert.DoesNotContain(list, h => h.IsSubstituteDay);
        Assert.True(BaHolidays.State.IsHoliday(new DateOnly(2026, 3, 1)));
        Assert.False(BaHolidays.State.IsHoliday(new DateOnly(2026, 3, 2)));
    }

    [Fact]
    public void Federation_2026_has_six_days()
    {
        var list = BaHolidays.Federation.InYear(2026);

        Assert.Equal(6, list.Count);
        Assert.Equal("FBIH", BaHolidays.Federation.Region);
        Assert.True(BaHolidays.Federation.IsHoliday(new DateOnly(2026, 5, 2)));
        Assert.True(BaHolidays.Federation.IsHoliday(new DateOnly(2026, 11, 25)));
    }

    [Fact]
    public void Federation_2027_moves_the_second_of_may_from_sunday_to_monday()
    {
        // 2 May 2027 is a Sunday. The 1 and 2 May days move when they fall on a Sunday (the ministry's notice).
        var list = BaHolidays.Federation.InYear(2027);
        var substitute = list.Single(h => h.IsSubstituteDay);

        Assert.Equal(7, list.Count);
        Assert.Equal(new DateOnly(2027, 5, 3), substitute.Date);
        Assert.Equal("Labour Day (second day) (day off in lieu)", substitute.EnglishName);
        Assert.Equal("Praznik rada", substitute.LocalName);
    }

    [Fact]
    public void Republika_Srpska_2026_has_sixteen_days_and_no_substitutes()
    {
        var list = BaHolidays.RepublikaSrpska.InYear(2026);

        Assert.Equal(16, list.Count);
        Assert.DoesNotContain(list, h => h.IsSubstituteDay);
        Assert.Equal("RS", BaHolidays.RepublikaSrpska.Region);
    }

    [Fact]
    public void Republika_Srpska_2026_orthodox_and_catholic_days_are_on_the_law_dates()
    {
        var calendar = BaHolidays.RepublikaSrpska;

        Assert.True(calendar.IsHoliday(new DateOnly(2026, 1, 6)));   // Orthodox Christmas Eve
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 1, 7)));   // Orthodox Christmas
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 3)));   // Catholic Good Friday
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 5)));   // Catholic Easter Sunday
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 6)));   // Catholic Easter Monday
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 10)));  // Orthodox Good Friday
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 12)));  // Orthodox Easter Sunday
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 13)));  // Orthodox Easter Monday
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 12, 24))); // Catholic Christmas Eve
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 12, 25))); // Catholic Christmas
    }

    [Fact]
    public void Republika_Srpska_has_no_9_January_day()
    {
        // Article 3(b), Dan Republike 9 January, has ceased to apply.
        Assert.False(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2026, 1, 9)));
        Assert.False(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2027, 1, 9)));
    }

    [Fact]
    public void Republika_Srpska_2027_moves_second_of_may_when_it_is_a_sunday()
    {
        // 2 May 2027 is a Sunday and Orthodox Easter Monday is 3 May 2027, so the day in lieu is 4 May.
        var list = BaHolidays.RepublikaSrpska.InYear(2027);
        var substitute = list.Single(h => h.IsSubstituteDay);

        Assert.Equal(17, list.Count);
        Assert.Equal(new DateOnly(2027, 5, 4), substitute.Date);
        Assert.Equal("International Labour Day (second day) (day off in lieu)", substitute.EnglishName);
        Assert.Equal("Međunarodni praznik rada (drugi dan)", substitute.LocalName);
    }

    [Fact]
    public void Republika_Srpska_2025_and_2027_easter_dates()
    {
        // Orthodox and Catholic Easter both fall on 20 April 2025, so both Good Fridays are 18 April.
        Assert.True(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2025, 4, 18)));
        Assert.True(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2025, 4, 21)));
        // 2027: Catholic Easter 28 March, Orthodox Easter 2 May.
        Assert.True(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2027, 3, 26)));
        Assert.True(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2027, 3, 29)));
        Assert.True(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2027, 4, 30)));
        Assert.True(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2027, 5, 3)));
    }

    [Fact]
    public void Republika_Srpska_2100_pins()
    {
        Assert.True(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2100, 3, 28))); // Catholic Easter Sunday
        Assert.True(BaHolidays.RepublikaSrpska.IsHoliday(new DateOnly(2100, 5, 2)));  // Orthodox Easter Sunday
    }

    [Fact]
    public void Albania_2026_has_thirteen_holidays_and_three_days_in_lieu()
    {
        var list = AlHolidays.Calendar.InYear(2026);
        var substitutes = list.Where(h => h.IsSubstituteDay).Select(h => h.Date).ToArray();

        Assert.Equal(16, list.Count);
        // Nevruz (22 March), Alphabet Day (22 November) and Liberation Day (29 November) fall on Sundays in 2026.
        Assert.Equal(new[] { new DateOnly(2026, 3, 23), new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 30) }, substitutes);
    }

    [Fact]
    public void Albania_2026_saturday_holidays_are_not_moved()
    {
        // Summer Day (14 March), Mother Teresa Day (5 September) and Independence Day (28 November) are Saturdays in 2026.
        var calendar = AlHolidays.Calendar;

        Assert.True(calendar.IsHoliday(new DateOnly(2026, 3, 14)));
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 9, 5)));
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 11, 28)));
        Assert.False(calendar.IsHoliday(new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void Albania_moves_a_sunday_holiday_to_monday()
    {
        var calendar = AlHolidays.Calendar;

        Assert.True(calendar.IsHoliday(new DateOnly(2026, 3, 23)));
        Assert.False(calendar.IsHoliday(new DateOnly(2026, 3, 24)));
    }

    [Fact]
    public void Albania_easter_dates_2025_2026_2027()
    {
        var calendar = AlHolidays.Calendar;

        // 2025: both Easter Sundays are 20 April.
        Assert.True(calendar.IsHoliday(new DateOnly(2025, 4, 20)));
        // 2026: Catholic 5 April, Orthodox 12 April.
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 5)));
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 12)));
        // 2027: Catholic 28 March, Orthodox 2 May.
        Assert.True(calendar.IsHoliday(new DateOnly(2027, 3, 28)));
        Assert.True(calendar.IsHoliday(new DateOnly(2027, 5, 2)));
    }

    [Fact]
    public void Albania_2100_pins()
    {
        Assert.True(AlHolidays.Calendar.IsHoliday(new DateOnly(2100, 3, 28))); // Catholic Easter Sunday
        Assert.True(AlHolidays.Calendar.IsHoliday(new DateOnly(2100, 5, 2)));  // Orthodox Easter Sunday
    }

    [Fact]
    public void Kosovo_2026_has_ten_holidays()
    {
        var list = XkHolidays.Calendar.InYear(2026);

        Assert.Equal(10, list.Count);
        Assert.DoesNotContain(list, h => h.IsSubstituteDay);
    }

    [Fact]
    public void Kosovo_2026_has_the_law_dates()
    {
        var calendar = XkHolidays.Calendar;

        Assert.True(calendar.IsHoliday(new DateOnly(2026, 1, 1)));
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 1, 2)));
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 1, 7)));   // Orthodox Christmas
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 2, 17)));  // Independence Day
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 9)));   // Constitution Day
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 6)));   // Catholic Easter Monday
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 4, 13)));  // Orthodox Easter Monday
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 5, 1)));   // Labour Day
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 5, 9)));   // Europe Day (a Saturday: not moved)
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 12, 25))); // Catholic Christmas
        Assert.False(calendar.IsHoliday(new DateOnly(2026, 4, 7)));
    }

    [Fact]
    public void Kosovo_2100_pins()
    {
        Assert.True(XkHolidays.Calendar.IsHoliday(new DateOnly(2100, 3, 29))); // Catholic Easter Monday
        Assert.True(XkHolidays.Calendar.IsHoliday(new DateOnly(2100, 5, 3)));  // Orthodox Easter Monday
    }

    private static IEnumerable<IPublicHolidayCalendar> All()
    {
        yield return BaHolidays.State;
        yield return BaHolidays.Federation;
        yield return BaHolidays.RepublikaSrpska;
        yield return AlHolidays.Calendar;
        yield return XkHolidays.Calendar;
    }

    private static void AssertSorted(IReadOnlyList<PublicHoliday> list)
    {
        for (var i = 1; i < list.Count; i++)
        {
            Assert.True(list[i - 1].Date <= list[i].Date);
        }
    }
}
