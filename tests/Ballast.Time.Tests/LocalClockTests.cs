// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Time.Tests;

/// <summary>
/// <see cref="LocalClock"/> for Europe/Zagreb, with the time set by the test. Zagreb is UTC+1 in winter and UTC+2 in
/// summer; the clocks switch on 2026-03-29 (02:00 to 03:00 local) and on 2026-10-25 (03:00 back to 02:00 local).
/// </summary>
public class LocalClockTests
{
    private const string ZagrebId = "Europe/Zagreb";

    // 2026-07-15 10:20 UTC is 12:20 in Zagreb (summer time). A fixed instant, never the real clock.
    private static readonly DateTime FixedUtcNow = Utc(2026, 7, 15, 10, 20);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static LocalClock ClockAt(DateTime utcNow, out FakeTimeProvider time)
    {
        time = new FakeTimeProvider(new DateTimeOffset(utcNow, TimeSpan.Zero));
        return new LocalClock(ZagrebId, time);
    }

    // -- construction ----------------------------------------------------------------------------------

    [Fact]
    public void A_zone_this_machine_does_not_know_stops_the_clock_at_construction()
    {
        var ex = Assert.Throws<ArgumentException>(() => new LocalClock("Not/AZone", TimeProvider.System));

        Assert.Equal("zoneId", ex.ParamName);
        Assert.Contains("Not/AZone", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../../etc/passwd")]
    public void A_blank_or_malformed_zone_stops_the_clock_at_construction(string zoneId)
    {
        Assert.Throws<ArgumentException>(() => new LocalClock(zoneId, TimeProvider.System));
    }

    [Fact]
    public void A_null_zone_or_a_null_time_provider_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalClock(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new LocalClock(ZagrebId, null!));
    }

    [Fact]
    public void A_windows_zone_id_is_accepted_and_the_zone_is_kept()
    {
        var clock = new LocalClock("Central European Standard Time", TimeProvider.System);

        Assert.NotNull(clock.Zone);
    }

    [Fact]
    public void The_zone_is_the_zone_it_was_given()
    {
        Assert.Equal(ZagrebId, ClockAt(FixedUtcNow, out _).Zone.Id);
    }

    // -- now and today ---------------------------------------------------------------------------------

    [Fact]
    public void Today_InWinter_ChangesAtZagrebMidnight()
    {
        var clock = ClockAt(Utc(2026, 1, 15, 22, 59), out var time);
        Assert.Equal(new DateOnly(2026, 1, 15), clock.Today);

        time.Set(Utc(2026, 1, 15, 23, 0));
        Assert.Equal(new DateOnly(2026, 1, 16), clock.Today);
    }

    [Fact]
    public void Today_InSummer_ChangesAtZagrebMidnight()
    {
        var clock = ClockAt(Utc(2026, 7, 15, 21, 59), out var time);
        Assert.Equal(new DateOnly(2026, 7, 15), clock.Today);

        time.Set(Utc(2026, 7, 15, 22, 0));
        Assert.Equal(new DateOnly(2026, 7, 16), clock.Today);
    }

    [Fact]
    public void Now_OnTheSpringSwitchDay_SkipsFromTwoToThree()
    {
        var clock = ClockAt(Utc(2026, 3, 28, 23, 0), out var time);
        Assert.Equal(new DateTime(2026, 3, 29, 0, 0, 0), clock.Now);

        time.Set(Utc(2026, 3, 29, 0, 59));
        Assert.Equal(new DateTime(2026, 3, 29, 1, 59, 0), clock.Now);

        time.Set(Utc(2026, 3, 29, 1, 0));
        Assert.Equal(new DateTime(2026, 3, 29, 3, 0, 0), clock.Now);
    }

    [Fact]
    public void Now_OnTheAutumnSwitchDay_RepeatsTheTwoOClockHour()
    {
        var clock = ClockAt(Utc(2026, 10, 24, 22, 0), out var time);
        Assert.Equal(new DateTime(2026, 10, 25, 0, 0, 0), clock.Now);

        time.Set(Utc(2026, 10, 25, 0, 30));
        Assert.Equal(new DateTime(2026, 10, 25, 2, 30, 0), clock.Now);

        time.Set(Utc(2026, 10, 25, 1, 30));
        Assert.Equal(new DateTime(2026, 10, 25, 2, 30, 0), clock.Now);
    }

    [Fact]
    public void Now_is_a_wall_clock_reading_not_an_instant()
    {
        Assert.Equal(DateTimeKind.Unspecified, ClockAt(FixedUtcNow, out _).Now.Kind);
    }

    // -- dates of instants -----------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-10-24T21:59:00Z", 2026, 10, 24)]
    [InlineData("2026-10-24T22:00:00Z", 2026, 10, 25)]
    [InlineData("2026-12-31T23:30:00Z", 2027, 1, 1)]
    public void DateOf_names_the_zagreb_day_an_instant_falls_on(string utcInstant, int year, int month, int day)
    {
        var instant = DateTimeOffset.Parse(utcInstant, CultureInfo.InvariantCulture).UtcDateTime;
        var clock = ClockAt(FixedUtcNow, out _);

        Assert.Equal(new DateOnly(year, month, day), clock.DateOf(instant));
    }

    [Fact]
    public void DateOf_refuses_a_local_kind_value()
    {
        var clock = ClockAt(FixedUtcNow, out _);
        var local = new DateTime(2026, 1, 15, 22, 59, 0, DateTimeKind.Local);

        var ex = Assert.Throws<ArgumentException>(() => clock.DateOf(local));

        Assert.Equal("instant", ex.ParamName);
    }

    [Fact]
    public void DateOf_reads_an_unspecified_kind_as_utc()
    {
        var clock = ClockAt(FixedUtcNow, out _);
        var unspecified = DateTime.SpecifyKind(Utc(2026, 12, 31, 23, 30), DateTimeKind.Unspecified);

        Assert.Equal(new DateOnly(2027, 1, 1), clock.DateOf(unspecified));
    }

    // -- slots -----------------------------------------------------------------------------------------

    [Fact]
    public void ToUtc_InSummer_UsesTheSummerOffset()
    {
        var instant = ClockAt(FixedUtcNow, out _).ToUtc(new DateOnly(2026, 7, 15), 9);

        Assert.Equal(Utc(2026, 7, 15, 7), instant);
        Assert.Equal(DateTimeKind.Utc, instant.Kind);
    }

    [Fact]
    public void ToUtc_InWinter_UsesTheWinterOffset()
    {
        Assert.Equal(Utc(2026, 1, 15, 8), ClockAt(FixedUtcNow, out _).ToUtc(new DateOnly(2026, 1, 15), 9));
    }

    // The day is always 29 March 2026. The expected instant is given as its UTC date and hour: 00:00 local is still winter
    // time (23:00 UTC the day before), 03:00 local is summer time (01:00 UTC).
    [Theory]
    [InlineData(0, 2026, 3, 28, 23)]
    [InlineData(1, 2026, 3, 29, 0)]
    [InlineData(3, 2026, 3, 29, 1)]
    [InlineData(23, 2026, 3, 29, 21)]
    public void ToUtc_OnTheSpringSwitchDay_ReadsTheHoursAroundTheSkip(int hour, int expectedYear, int expectedMonth, int expectedDay, int expectedUtcHour)
    {
        var expected = new DateTime(expectedYear, expectedMonth, expectedDay, expectedUtcHour, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, ClockAt(FixedUtcNow, out _).ToUtc(new DateOnly(2026, 3, 29), hour));
    }

    [Fact]
    public void ToUtc_OnTheSpringSwitchDay_TheSkippedHourTwo_IsTheFirstValidInstantAfterTheGap()
    {
        Assert.Equal(Utc(2026, 3, 29, 1), ClockAt(FixedUtcNow, out _).ToUtc(new DateOnly(2026, 3, 29), 2));
    }

    [Theory]
    [InlineData(0, 2026, 10, 24, 22)]
    [InlineData(1, 2026, 10, 24, 23)]
    [InlineData(3, 2026, 10, 25, 2)]
    [InlineData(23, 2026, 10, 25, 22)]
    public void ToUtc_OnTheAutumnSwitchDay_ReadsTheHoursAroundTheRepeat(int hour, int expectedYear, int expectedMonth, int expectedDay, int expectedUtcHour)
    {
        var expected = new DateTime(expectedYear, expectedMonth, expectedDay, expectedUtcHour, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, ClockAt(FixedUtcNow, out _).ToUtc(new DateOnly(2026, 10, 25), hour));
    }

    [Fact]
    public void ToUtc_OnTheAutumnSwitchDay_TheRepeatedHourTwo_IsTheEarlierInstant()
    {
        Assert.Equal(Utc(2026, 10, 25, 0), ClockAt(FixedUtcNow, out _).ToUtc(new DateOnly(2026, 10, 25), 2));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(-1)]
    public void ToUtc_an_hour_outside_the_day_is_refused(int hour)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ClockAt(FixedUtcNow, out _).ToUtc(new DateOnly(2026, 7, 15), hour));

        Assert.Equal("hour", ex.ParamName);
    }

    [Fact]
    public void IsPast_YesterdayAtHour23_IsPast()
    {
        Assert.True(ClockAt(FixedUtcNow, out _).IsPast(new DateOnly(2026, 7, 14), new TimeOnly(23, 0)));
    }

    [Fact]
    public void IsPast_TodayAtTheCurrentHour_IsPast()
    {
        // The running hour counts as past, as it does for a slot in progress.
        Assert.True(ClockAt(FixedUtcNow, out _).IsPast(new DateOnly(2026, 7, 15), new TimeOnly(12, 0)));
    }

    [Fact]
    public void IsPast_TodayAtTheNextHour_IsNotPast()
    {
        Assert.False(ClockAt(FixedUtcNow, out _).IsPast(new DateOnly(2026, 7, 15), new TimeOnly(13, 0)));
    }

    [Fact]
    public void IsPast_TomorrowAtHour0_IsNotPast()
    {
        Assert.False(ClockAt(FixedUtcNow, out _).IsPast(new DateOnly(2026, 7, 16), new TimeOnly(0, 0)));
    }

    [Fact]
    public void IsPast_a_slot_that_starts_exactly_now_is_past_and_one_a_minute_later_is_not()
    {
        var clock = ClockAt(Utc(2026, 7, 15, 10, 20), out _);

        Assert.True(clock.IsPast(new DateOnly(2026, 7, 15), new TimeOnly(12, 20)));
        Assert.False(clock.IsPast(new DateOnly(2026, 7, 15), new TimeOnly(12, 21)));
    }
}
