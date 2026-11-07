// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time.Tests;

/// <summary>
/// The registry (<see cref="PublicHolidayCalendar"/>): which countries and regions have a calendar, the lookup rules, and
/// the range every registered calendar covers. Dates were checked against their weekdays: 2026-02-15 is a Sunday, 2026-05-02
/// a Saturday, 2026-01-01 a Thursday.
/// </summary>
public class PublicHolidayCalendarTests
{
    private static IPublicHolidayCalendar Require(string? country, string? region = null) =>
        PublicHolidayCalendar.For(country, region) ?? throw new InvalidOperationException($"No calendar for {country}/{region}.");

    public static TheoryData<string, string?> RegisteredCalendars => new()
    {
        { "AT", null },
        { "CZ", null },
        { "HR", null },
        { "HU", null },
        { "IT", null },
        { "RS", null },
        { "SI", null },
    };

    private static readonly string[] RegisteredCountries = ["AT", "CZ", "HR", "HU", "IT", "RS", "SI"];

    [Fact]
    public void SupportedCountriesAreTheRegisteredCountriesInSortedOrder()
    {
        Assert.Equal(RegisteredCountries, PublicHolidayCalendar.SupportedCountries);
    }

    [Theory]
    [MemberData(nameof(RegisteredCalendars))]
    public void EveryRegisteredCalendarCoversTheSameRangeAndHasAScope(string country, string? region)
    {
        IPublicHolidayCalendar calendar = Require(country, region);

        Assert.Equal(country, calendar.CountryIso2);
        Assert.Equal(region, calendar.Region);
        Assert.Equal(2020, calendar.FirstYear);
        Assert.Equal(2100, calendar.LastYear);
        Assert.False(string.IsNullOrWhiteSpace(calendar.Scope));
    }

    [Theory]
    [MemberData(nameof(RegisteredCalendars))]
    public void EveryRegisteredCalendarListsEachYearSortedByDate(string country, string? region)
    {
        IPublicHolidayCalendar calendar = Require(country, region);

        foreach (int year in new[] { 2020, 2025, 2026, 2027, 2100 })
        {
            IReadOnlyList<PublicHoliday> holidays = calendar.InYear(year);

            Assert.NotEmpty(holidays);
            Assert.All(holidays, holiday => Assert.Equal(year, holiday.Date.Year));
            for (int i = 1; i < holidays.Count; i++)
            {
                Assert.True(holidays[i - 1].Date <= holidays[i].Date, $"{country}/{region} {year} is not sorted.");
            }
        }
    }

    [Fact]
    public void ForIgnoresCaseAndSurroundingWhiteSpace()
    {
        Assert.Same(Require("HR"), Require("hr"));
        Assert.Same(Require("HR"), Require(" HR "));
        Assert.Same(Require("RS"), Require("rs"));
        Assert.Same(Require("RS"), Require(" RS "));
    }

    [Fact]
    public void ForGivesNullForAMissingOrUnknownCountry()
    {
        Assert.Null(PublicHolidayCalendar.For(null));
        Assert.Null(PublicHolidayCalendar.For(string.Empty));
        Assert.Null(PublicHolidayCalendar.For("   "));
        Assert.Null(PublicHolidayCalendar.For("XX"));
        Assert.Null(PublicHolidayCalendar.For("HRV"));
    }

    [Theory]
    [InlineData("CH")]
    [InlineData("DE")]
    [InlineData("SK")]
    [InlineData("ME")]
    [InlineData("MK")]
    [InlineData("AL")]
    [InlineData("XK")]
    public void ACountryWithoutAConfirmedLawIsNotRegistered(string country)
    {
        Assert.Null(PublicHolidayCalendar.For(country));
        Assert.DoesNotContain(country, PublicHolidayCalendar.SupportedCountries);
    }

    [Fact]
    public void BosniaAndHerzegovinaHasNoRegisteredCalendarAtAnyLevel()
    {
        // Republika Srpska's Sunday rule and believers-only days are not confirmed, so it is not registered.
        Assert.Null(PublicHolidayCalendar.For("BA"));
        Assert.Null(PublicHolidayCalendar.For("BA", string.Empty));
        Assert.Null(PublicHolidayCalendar.For("BA", "RS"));
        Assert.Null(PublicHolidayCalendar.For("BA", "FBIH"));
        Assert.Null(PublicHolidayCalendar.For("BA", "BD"));
    }

    [Fact]
    public void ARegionTheCountryDoesNotListGivesNull()
    {
        Assert.Null(PublicHolidayCalendar.For("HR", "X"));
        Assert.Null(PublicHolidayCalendar.For("HR", string.Empty));
        Assert.Null(PublicHolidayCalendar.For("HR", "   "));
        Assert.Null(PublicHolidayCalendar.For(null, "RS"));
        Assert.Null(PublicHolidayCalendar.For("XX", "RS"));
    }

    [Fact]
    public void ForWithANullRegionIsTheNationalCalendar()
    {
        Assert.Same(PublicHolidayCalendar.For("HR"), PublicHolidayCalendar.For("HR", null));
    }

    [Fact]
    public void ACalendarOutsideItsRangeThrowsAndNeverGivesAGuess()
    {
        IPublicHolidayCalendar calendar = Require("HR");

        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.InYear(2019));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.InYear(2101));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.IsHoliday(new DateOnly(2019, 12, 31)));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.IsHoliday(new DateOnly(2101, 1, 1)));
    }

    [Fact]
    public void CroatiaKeepsItsFixedAndEasterDays()
    {
        IPublicHolidayCalendar croatia = Require("HR");

        Assert.True(croatia.IsHoliday(new DateOnly(2026, 12, 25)));
        Assert.True(croatia.IsHoliday(new DateOnly(2026, 4, 6)));   // Easter Monday; Western Easter 2026 is 5 April
        Assert.False(croatia.IsHoliday(new DateOnly(2026, 12, 24)));
    }

    [Fact]
    public void SerbiaGivesTheSundayHolidayADayInLieuOnTheNextFreeWorkingDay()
    {
        IPublicHolidayCalendar serbia = Require("RS");

        // 15 February 2026 is a Sunday. 16 February is the second day of the holiday, so the day in lieu is 17 February.
        Assert.True(serbia.IsHoliday(new DateOnly(2026, 2, 15)));
        Assert.True(serbia.IsHoliday(new DateOnly(2026, 2, 16)));
        Assert.True(serbia.IsHoliday(new DateOnly(2026, 2, 17)));
        Assert.False(serbia.IsHoliday(new DateOnly(2026, 2, 18)));

        PublicHoliday lieu = serbia.InYear(2026).Single(holiday => holiday.Date == new DateOnly(2026, 2, 17));
        Assert.True(lieu.IsSubstituteDay);
        Assert.EndsWith(" (day off in lieu)", lieu.EnglishName, StringComparison.Ordinal);
    }

    [Fact]
    public void SerbiaListsTheOrthodoxEasterMondayAfterItsDate()
    {
        // Orthodox Easter 2026 is 12 April, so its Monday is 13 April.
        Assert.True(Require("RS").IsHoliday(new DateOnly(2026, 4, 13)));
    }

}
