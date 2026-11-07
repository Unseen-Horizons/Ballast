// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time.Tests;

/// <summary>
/// The rule engine (<see cref="RuleBasedCalendar"/> and <see cref="HolidayRule"/>) with test-local rules. The calendar
/// dates used here were checked against the weekday they fall on: 2022-05-01 is a Sunday, 2022-05-07 a Saturday,
/// 2026-02-15 a Sunday, 2026-04-12 a Sunday (Orthodox Easter), and 2025-04-20 is both Western and Orthodox Easter.
/// </summary>
public class RuleBasedCalendarTests
{
    private static RuleBasedCalendar Calendar(bool sundayMoves, params HolidayRule[] rules) =>
        new("XX", null, "Test list.", rules, sundayMoves);

    // -- construction and properties --------------------------------------------------------------------

    [Fact]
    public void PropertiesAreTheConstructorArgumentsAndTheSupportedRange()
    {
        RuleBasedCalendar calendar = new("HR", "ZG", "Only what the test says.", [], false);

        Assert.Equal("HR", calendar.CountryIso2);
        Assert.Equal("ZG", calendar.Region);
        Assert.Equal("Only what the test says.", calendar.Scope);
        Assert.Equal(2020, calendar.FirstYear);
        Assert.Equal(2100, calendar.LastYear);
    }

    [Fact]
    public void ANullRegionMeansTheCountrysOwnCalendar()
    {
        Assert.Null(Calendar(false).Region);
    }

    [Theory]
    [InlineData("hr")]
    [InlineData("HRV")]
    [InlineData("H1")]
    [InlineData("")]
    public void ACountryCodeThatIsNotTwoUpperCaseLettersIsRefused(string iso2)
    {
        Assert.Throws<ArgumentException>(() => { _ = new RuleBasedCalendar(iso2, null, "Scope.", [], false); });
    }

    [Fact]
    public void AnEmptyRegionOrScopeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => { _ = new RuleBasedCalendar("HR", string.Empty, "Scope.", [], false); });
        Assert.Throws<ArgumentException>(() => { _ = new RuleBasedCalendar("HR", null, " ", [], false); });
    }

    [Fact]
    public void NullRulesAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => { _ = new RuleBasedCalendar("HR", null, "Scope.", null!, false); });
    }

    // -- rule factories ---------------------------------------------------------------------------------

    [Fact]
    public void AFixedRuleGivesItsMonthAndDayInEveryYear()
    {
        HolidayRule rule = HolidayRule.Fixed(4, 30, "Test", "Test");

        Assert.Equal(new DateOnly(2026, 4, 30), rule.DateIn(2026));
        Assert.Equal(new DateOnly(2100, 4, 30), rule.DateIn(2100));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(4, 0)]
    [InlineData(4, 31)]
    [InlineData(2, 29)]
    [InlineData(6, 31)]
    public void AFixedRuleOutsideACommonYearsDatesIsRefused(int month, int day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = HolidayRule.Fixed(month, day, "Test", "Test"); });
    }

    [Fact]
    public void AFixedRuleOnTheLastDayOfAMonthIsAccepted()
    {
        Assert.Equal(new DateOnly(2026, 2, 28), HolidayRule.Fixed(2, 28, "Test", "Test").DateIn(2026));
        Assert.Equal(new DateOnly(2026, 12, 31), HolidayRule.Fixed(12, 31, "Test", "Test").DateIn(2026));
    }

    [Fact]
    public void AnEasterRuleGivesItsOffsetFromEaster()
    {
        Assert.Equal(new DateOnly(2026, 4, 3), HolidayRule.WesternEaster(-2, "Test", "Test").DateIn(2026));
        Assert.Equal(new DateOnly(2026, 4, 6), HolidayRule.WesternEaster(1, "Test", "Test").DateIn(2026));
        Assert.Equal(new DateOnly(2026, 4, 10), HolidayRule.OrthodoxEaster(-2, "Test", "Test").DateIn(2026));
        Assert.Equal(new DateOnly(2026, 4, 13), HolidayRule.OrthodoxEaster(1, "Test", "Test").DateIn(2026));
    }

    [Fact]
    public void AHolidayNameThatIsBlankIsRefused()
    {
        Assert.Throws<ArgumentException>(() => { _ = HolidayRule.Fixed(1, 1, " ", "Test"); });
        Assert.Throws<ArgumentException>(() => { _ = HolidayRule.WesternEaster(0, "Test", string.Empty); });
        Assert.Throws<ArgumentException>(() => { _ = HolidayRule.OrthodoxEaster(0, "  ", "Test"); });
    }

    // -- day in lieu ------------------------------------------------------------------------------------

    [Fact]
    public void ASundayHolidayGetsADayInLieuOnTheMonday()
    {
        // 2022-05-01 is a Sunday.
        RuleBasedCalendar calendar = Calendar(true, HolidayRule.Fixed(5, 1, "Praznik", "Labour Day", substitutable: true));

        IReadOnlyList<PublicHoliday> year = calendar.InYear(2022);

        Assert.Equal(2, year.Count);
        Assert.Equal(new PublicHoliday(new DateOnly(2022, 5, 1), "Praznik", "Labour Day"), year[0]);
        Assert.Equal(
            new PublicHoliday(new DateOnly(2022, 5, 2), "Praznik", "Labour Day (day off in lieu)", IsSubstituteDay: true),
            year[1]);
    }

    [Fact]
    public void TheDayInLieuSkipsAHolidayAlreadyOnTheMonday()
    {
        // The Serbian pattern: 2026-02-15 is a Sunday and 2026-02-16 is itself a holiday, so the day in lieu is 02-17.
        RuleBasedCalendar calendar = Calendar(
            true,
            HolidayRule.Fixed(2, 15, "Sretenje", "Statehood Day", substitutable: true),
            HolidayRule.Fixed(2, 16, "Sretenje", "Statehood Day (second day)", substitutable: true));

        IReadOnlyList<PublicHoliday> year = calendar.InYear(2026);

        Assert.Equal(3, year.Count);
        Assert.Equal(new DateOnly(2026, 2, 15), year[0].Date);
        Assert.Equal(new DateOnly(2026, 2, 16), year[1].Date);
        Assert.Equal(new DateOnly(2026, 2, 17), year[2].Date);
        Assert.True(year[2].IsSubstituteDay);
        Assert.False(year[0].IsSubstituteDay);
        Assert.False(year[1].IsSubstituteDay);
    }

    [Fact]
    public void ASaturdayHolidayIsNotMoved()
    {
        // 2022-05-07 is a Saturday.
        RuleBasedCalendar calendar = Calendar(true, HolidayRule.Fixed(5, 7, "Test", "Test", substitutable: true));

        IReadOnlyList<PublicHoliday> year = calendar.InYear(2022);

        Assert.Single(year);
        Assert.False(year[0].IsSubstituteDay);
    }

    [Fact]
    public void AHolidayThatIsNotSubstitutableIsNotMoved()
    {
        RuleBasedCalendar calendar = Calendar(true, HolidayRule.Fixed(5, 1, "Test", "Test"));

        IReadOnlyList<PublicHoliday> year = calendar.InYear(2022);

        Assert.Single(year);
    }

    [Fact]
    public void ASundayCalendarThatDoesNotMoveDaysAddsNothing()
    {
        RuleBasedCalendar calendar = Calendar(false, HolidayRule.Fixed(5, 1, "Test", "Test", substitutable: true));

        Assert.Single(calendar.InYear(2022));
    }

    [Fact]
    public void AnOrthodoxEasterSundayHolidayMovesToTheFirstFreeWorkingDay()
    {
        // 2026-04-12 is Orthodox Easter Sunday and 2026-04-13 is already Easter Monday, so the day in lieu is 04-14.
        RuleBasedCalendar calendar = Calendar(
            true,
            HolidayRule.OrthodoxEaster(0, "Vaskrs", "Orthodox Easter Sunday", substitutable: true),
            HolidayRule.OrthodoxEaster(1, "Vaskršnji ponedeljak", "Orthodox Easter Monday"));

        IReadOnlyList<PublicHoliday> year = calendar.InYear(2026);

        Assert.Equal(3, year.Count);
        Assert.Equal(new DateOnly(2026, 4, 12), year[0].Date);
        Assert.Equal(new DateOnly(2026, 4, 13), year[1].Date);
        Assert.Equal(new DateOnly(2026, 4, 14), year[2].Date);
        Assert.Equal("Vaskrs", year[2].LocalName);
        Assert.Equal("Orthodox Easter Sunday (day off in lieu)", year[2].EnglishName);
        Assert.True(year[2].IsSubstituteDay);
    }

    // -- listing and lookup -----------------------------------------------------------------------------

    [Fact]
    public void InYearIsSortedByDateWhateverTheRuleOrder()
    {
        RuleBasedCalendar calendar = Calendar(
            false,
            HolidayRule.Fixed(12, 25, "Christmas", "Christmas Day"),
            HolidayRule.WesternEaster(0, "Easter", "Easter Sunday"),
            HolidayRule.Fixed(1, 1, "New Year", "New Year's Day"));

        IReadOnlyList<PublicHoliday> year = calendar.InYear(2026);

        Assert.Equal(new DateOnly(2026, 1, 1), year[0].Date);
        Assert.Equal(new DateOnly(2026, 4, 5), year[1].Date);
        Assert.Equal(new DateOnly(2026, 12, 25), year[2].Date);
    }

    [Fact]
    public void TwoHolidaysOnOneDateAreBothListed()
    {
        // 2025: Western and Orthodox Easter are both 20 April. Both are kept, with their own names.
        RuleBasedCalendar calendar = Calendar(
            false,
            HolidayRule.WesternEaster(0, "Pashka Katolike", "Catholic Easter Sunday"),
            HolidayRule.OrthodoxEaster(0, "Pashka Ortodokse", "Orthodox Easter Sunday"));

        IReadOnlyList<PublicHoliday> year = calendar.InYear(2025);

        Assert.Equal(2, year.Count);
        Assert.Equal(new DateOnly(2025, 4, 20), year[0].Date);
        Assert.Equal(new DateOnly(2025, 4, 20), year[1].Date);
        Assert.Equal("Pashka Katolike", year[0].LocalName);
        Assert.Equal("Pashka Ortodokse", year[1].LocalName);
    }

    [Fact]
    public void IsHolidayIsTrueOnAHolidayAndFalseTheDayAfter()
    {
        RuleBasedCalendar calendar = Calendar(false, HolidayRule.Fixed(5, 1, "Test", "Test"));

        Assert.True(calendar.IsHoliday(new DateOnly(2026, 5, 1)));
        Assert.False(calendar.IsHoliday(new DateOnly(2026, 5, 2)));
        Assert.False(calendar.IsHoliday(new DateOnly(2026, 4, 30)));
    }

    [Fact]
    public void IsHolidayCountsADayInLieu()
    {
        RuleBasedCalendar calendar = Calendar(true, HolidayRule.Fixed(5, 1, "Test", "Test", substitutable: true));

        Assert.True(calendar.IsHoliday(new DateOnly(2022, 5, 2)));
    }

    [Theory]
    [InlineData(2019)]
    [InlineData(2101)]
    public void AYearOutsideTheSupportedRangeIsRefusedByInYear(int year)
    {
        RuleBasedCalendar calendar = Calendar(false, HolidayRule.Fixed(5, 1, "Test", "Test"));

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = calendar.InYear(year); });
    }

    [Theory]
    [InlineData(2019, 12, 31)]
    [InlineData(2101, 1, 1)]
    public void ADateOutsideTheSupportedRangeIsRefusedByIsHoliday(int year, int month, int day)
    {
        RuleBasedCalendar calendar = Calendar(false, HolidayRule.Fixed(5, 1, "Test", "Test"));

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = calendar.IsHoliday(new DateOnly(year, month, day)); });
    }

    [Theory]
    [InlineData(2020)]
    [InlineData(2100)]
    public void TheBoundYearsAreAnswered(int year)
    {
        RuleBasedCalendar calendar = Calendar(false, HolidayRule.WesternEaster(0, "Easter", "Easter Sunday"));

        Assert.Single(calendar.InYear(year));
    }

    [Fact]
    public void EasterRulesGiveTheReferenceDatesIn2100()
    {
        RuleBasedCalendar calendar = Calendar(
            false,
            HolidayRule.WesternEaster(0, "Easter", "Easter Sunday"),
            HolidayRule.OrthodoxEaster(0, "Vaskrs", "Orthodox Easter Sunday"));

        IReadOnlyList<PublicHoliday> year = calendar.InYear(2100);

        Assert.Equal(new DateOnly(2100, 3, 28), year[0].Date);
        Assert.Equal(new DateOnly(2100, 5, 2), year[1].Date);
    }
}
