// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time.Tests;

/// <summary>
/// <see cref="Easter"/> against the reference table of the holiday plan, the dates derived from it, and the year bounds.
/// The reference dates were derived by hand from the algorithms, not read from a table of Easter dates.
/// </summary>
public class EasterTests
{
    [Theory]
    [InlineData(2020, 4, 12)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2100, 3, 28)]
    public void WesternEasterMatchesTheReferenceTable(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), Easter.Western(year));
    }

    [Theory]
    [InlineData(2020, 4, 19)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 12)]
    [InlineData(2027, 5, 2)]
    [InlineData(2100, 5, 2)]
    public void OrthodoxEasterMatchesTheReferenceTable(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), Easter.Orthodox(year));
    }

    [Fact]
    public void WesternMovableDaysDerivedFromEasterAreRight()
    {
        // 2025: Good Friday, Easter Monday, Ascension (+39), Pentecost (+49), Whit Monday (+50), Corpus Christi (+60).
        DateOnly easter2025 = Easter.Western(2025);
        Assert.Equal(new DateOnly(2025, 4, 18), easter2025.AddDays(-2));
        Assert.Equal(new DateOnly(2025, 4, 21), easter2025.AddDays(1));
        Assert.Equal(new DateOnly(2025, 5, 29), easter2025.AddDays(39));
        Assert.Equal(new DateOnly(2025, 6, 8), easter2025.AddDays(49));
        Assert.Equal(new DateOnly(2025, 6, 9), easter2025.AddDays(50));
        Assert.Equal(new DateOnly(2025, 6, 19), easter2025.AddDays(60));

        // 2026
        DateOnly easter2026 = Easter.Western(2026);
        Assert.Equal(new DateOnly(2026, 4, 3), easter2026.AddDays(-2));
        Assert.Equal(new DateOnly(2026, 4, 6), easter2026.AddDays(1));
        Assert.Equal(new DateOnly(2026, 5, 14), easter2026.AddDays(39));
        Assert.Equal(new DateOnly(2026, 5, 24), easter2026.AddDays(49));
        Assert.Equal(new DateOnly(2026, 5, 25), easter2026.AddDays(50));
        Assert.Equal(new DateOnly(2026, 6, 4), easter2026.AddDays(60));

        // 2027
        DateOnly easter2027 = Easter.Western(2027);
        Assert.Equal(new DateOnly(2027, 3, 26), easter2027.AddDays(-2));
        Assert.Equal(new DateOnly(2027, 3, 29), easter2027.AddDays(1));
        Assert.Equal(new DateOnly(2027, 5, 6), easter2027.AddDays(39));
        Assert.Equal(new DateOnly(2027, 5, 16), easter2027.AddDays(49));
        Assert.Equal(new DateOnly(2027, 5, 17), easter2027.AddDays(50));
        Assert.Equal(new DateOnly(2027, 5, 27), easter2027.AddDays(60));
    }

    [Fact]
    public void OrthodoxDaysDerivedFromEasterAreRight()
    {
        // 2026: Good Friday to Easter Monday.
        DateOnly easter2026 = Easter.Orthodox(2026);
        Assert.Equal(new DateOnly(2026, 4, 10), easter2026.AddDays(-2));
        Assert.Equal(new DateOnly(2026, 4, 11), easter2026.AddDays(-1));
        Assert.Equal(new DateOnly(2026, 4, 13), easter2026.AddDays(1));

        // 2027
        DateOnly easter2027 = Easter.Orthodox(2027);
        Assert.Equal(new DateOnly(2027, 4, 30), easter2027.AddDays(-2));
        Assert.Equal(new DateOnly(2027, 5, 1), easter2027.AddDays(-1));
        Assert.Equal(new DateOnly(2027, 5, 3), easter2027.AddDays(1));
    }

    [Fact]
    public void WesternAndOrthodoxCanFallOnTheSameDay()
    {
        // 2025 is the one year in the table where both are 20 April.
        Assert.Equal(Easter.Western(2025), Easter.Orthodox(2025));
    }

    [Theory]
    [InlineData(1582)]
    [InlineData(10000)]
    public void YearsOutsideTheGregorianRangeAreRefused(int year)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { Easter.Western(year); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { Easter.Orthodox(year); });
    }

    [Fact]
    public void BothBoundYearsAreAnswered()
    {
        Assert.Equal(DayOfWeek.Sunday, Easter.Western(1583).DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, Easter.Orthodox(1583).DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, Easter.Western(9999).DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, Easter.Orthodox(9999).DayOfWeek);
    }

    [Fact]
    public void EveryYearInTheRangeGivesASundayAndAWesternDateInItsWindow()
    {
        for (int year = 1583; year <= 9999; year++)
        {
            DateOnly western = Easter.Western(year);
            Assert.Equal(DayOfWeek.Sunday, western.DayOfWeek);

            // Western Easter always falls between 22 March and 25 April.
            Assert.True(western >= new DateOnly(year, 3, 22) && western <= new DateOnly(year, 4, 25), $"Western {year}");

            Assert.Equal(DayOfWeek.Sunday, Easter.Orthodox(year).DayOfWeek);
        }
    }

    [Fact]
    public void OrthodoxEasterFallsInItsWindowForTheCoveredCenturies()
    {
        // The Julian dates run from 22 March to 25 April, moved on by 13 or 14 days in the Gregorian calendar.
        for (int year = 1900; year <= 2100; year++)
        {
            DateOnly orthodox = Easter.Orthodox(year);
            Assert.True(orthodox >= new DateOnly(year, 3, 22) && orthodox <= new DateOnly(year, 5, 10), $"Orthodox {year}");
        }
    }
}
