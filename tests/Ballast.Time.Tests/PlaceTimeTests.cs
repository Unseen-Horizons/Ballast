// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Time.Tests;

/// <summary>
/// PlaceTime with every zone pinned: the viewer is always passed in, never the machine's. Zagreb is UTC+1 in winter and
/// UTC+2 in summer, London is UTC+0 and UTC+1, and Berlin has Zagreb's offsets on every date.
/// </summary>
public class PlaceTimeTests
{
    private static readonly TimeZoneInfo Zagreb = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly TimeZoneInfo LordHowe = TimeZoneInfo.FindSystemTimeZoneById("Australia/Lord_Howe");

    private static DateTime Utc(int y, int m, int d, int h, int min = 0, int s = 0, long extraTicks = 0) =>
        new DateTime(y, m, d, h, min, s, DateTimeKind.Utc).AddTicks(extraTicks);

    // -- clock text ------------------------------------------------------------------------------------

    [Fact]
    public void Zagreb_time_read_from_Zagreb_has_no_label()
    {
        Assert.Equal("14:05", PlaceTime.Clock(Utc(2026, 9, 10, 12, 5), Zagreb, Zagreb));
    }

    [Fact]
    public void Zagreb_time_read_from_London_carries_a_seasonal_label()
    {
        // Summer: Zagreb +2, London +1.
        Assert.Equal("14:05 GMT+2", PlaceTime.Clock(Utc(2026, 9, 10, 12, 5), Zagreb, London));
        // Winter: Zagreb +1, London +0.
        Assert.Equal("13:05 GMT+1", PlaceTime.Clock(Utc(2026, 1, 15, 12, 5), Zagreb, London));
    }

    [Fact]
    public void A_label_appears_only_when_the_offsets_differ()
    {
        // Berlin has the same offsets as Zagreb on every date, so no label. London differs, so it has one.
        Assert.Equal("14:05", PlaceTime.Clock(Utc(2026, 9, 10, 12, 5), Zagreb, Berlin));
        Assert.Equal("14:05 GMT+2", PlaceTime.Clock(Utc(2026, 9, 10, 12, 5), Zagreb, London));
    }

    [Fact]
    public void A_missing_place_zone_is_shown_in_the_viewers_zone_with_the_viewers_label()
    {
        // Never presented as the place's clock: the label is always there for an unknown zone.
        Assert.Equal("14:05 GMT+2", PlaceTime.Clock(Utc(2026, 9, 10, 12, 5), null, Zagreb));
        Assert.Equal("13:05 GMT+1", PlaceTime.Clock(Utc(2026, 9, 10, 12, 5), null, London));
    }

    [Fact]
    public void A_zone_id_that_does_not_resolve_gives_the_same_result_as_a_missing_zone()
    {
        var unresolved = Zones.Find("Not/AZone");

        Assert.Null(unresolved);
        Assert.Equal("13:05 GMT+1", PlaceTime.Clock(Utc(2026, 9, 10, 12, 5), unresolved, London));
    }

    [Fact]
    public void Clock_refuses_a_null_viewer_zone()
    {
        Assert.Throws<ArgumentNullException>(() => PlaceTime.Clock(Utc(2026, 9, 10, 12, 5), Zagreb, null!));
    }

    [Fact]
    public void Clock_range_labels_each_end_in_its_own_zone()
    {
        // Start in Zagreb (CEST), end in London (BST), read from Zagreb: only the London end differs.
        var range = PlaceTime.ClockRange(Utc(2026, 9, 10, 12, 0), Zagreb, Utc(2026, 9, 10, 14, 0), London, Zagreb);

        Assert.Equal("14:00–15:00 GMT+1", range);
    }

    [Fact]
    public void Clock_range_labels_an_end_with_no_zone_in_the_viewers_zone()
    {
        var range = PlaceTime.ClockRange(Utc(2026, 9, 10, 12, 0), Zagreb, Utc(2026, 9, 10, 14, 0), null, London);

        Assert.Equal("14:00 GMT+2–15:00 GMT+1", range);
    }

    [Fact]
    public void Spring_forward_day_is_read_with_the_right_offset_before_and_after_the_switch()
    {
        // 2026-03-29: Zagreb switches at 01:00 UTC (02:00 CET -> 03:00 CEST).
        Assert.Equal("00:30", PlaceTime.Clock(Utc(2026, 3, 28, 23, 30), Zagreb, Zagreb));
        Assert.Equal("01:59", PlaceTime.Clock(Utc(2026, 3, 29, 0, 59), Zagreb, Zagreb));
        Assert.Equal("03:00", PlaceTime.Clock(Utc(2026, 3, 29, 1, 0), Zagreb, Zagreb));
        // London is still on GMT until 2026-03-29 01:00 UTC too, so read from London across the switch the label is the
        // day's own offset difference.
        Assert.Equal("00:30 GMT+1", PlaceTime.Clock(Utc(2026, 3, 28, 23, 30), Zagreb, London));
    }

    [Fact]
    public void Fall_back_day_is_read_with_the_right_offset_before_and_after_the_switch()
    {
        // 2026-10-25: Zagreb switches at 01:00 UTC (03:00 CEST -> 02:00 CET).
        Assert.Equal("00:30", PlaceTime.Clock(Utc(2026, 10, 24, 22, 30), Zagreb, Zagreb));
        Assert.Equal("02:30", PlaceTime.Clock(Utc(2026, 10, 25, 0, 30), Zagreb, Zagreb));
        Assert.Equal("02:30", PlaceTime.Clock(Utc(2026, 10, 25, 1, 30), Zagreb, Zagreb));
    }

    [Fact]
    public void Offset_label_formats_half_hours_and_negative_offsets()
    {
        Assert.Equal("GMT+5:30", PlaceTime.OffsetLabel(TimeSpan.FromMinutes(330)));
        Assert.Equal("GMT-5", PlaceTime.OffsetLabel(TimeSpan.FromHours(-5)));
        Assert.Equal("GMT+0", PlaceTime.OffsetLabel(TimeSpan.Zero));
        Assert.Equal("GMT+1", PlaceTime.OffsetLabel(TimeSpan.FromHours(1)));
        Assert.Equal("GMT-3:30", PlaceTime.OffsetLabel(TimeSpan.FromMinutes(-210)));
        Assert.Equal("GMT+12", PlaceTime.OffsetLabel(TimeSpan.FromHours(12)));
        Assert.Equal("GMT+10", PlaceTime.OffsetLabel(TimeSpan.FromHours(10)));
    }

    [Fact]
    public void Offset_label_drops_a_seconds_part()
    {
        Assert.Equal("GMT+1", PlaceTime.OffsetLabel(TimeSpan.FromSeconds(3630)));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(-24)]
    [InlineData(30)]
    public void Offset_label_refuses_an_offset_of_a_day_or_more(int hours)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaceTime.OffsetLabel(TimeSpan.FromHours(hours)));
    }

    // -- wall time to instant --------------------------------------------------------------------------

    [Fact]
    public void Wall_time_at_00_30_on_the_switch_days_converts_to_the_right_instant()
    {
        Assert.Equal(Utc(2026, 3, 28, 23, 30), PlaceTime.WallToUtc(new DateTime(2026, 3, 29, 0, 30, 0), Zagreb));
        Assert.Equal(Utc(2026, 10, 24, 22, 30), PlaceTime.WallToUtc(new DateTime(2026, 10, 25, 0, 30, 0), Zagreb));
    }

    [Fact]
    public void Wall_time_in_the_spring_gap_moves_forward_an_hour()
    {
        // 02:30 on 2026-03-29 does not exist in Zagreb; it reads as 03:30 CEST = 01:30 UTC.
        var instant = PlaceTime.WallToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Zagreb);

        Assert.Equal(Utc(2026, 3, 29, 1, 30), instant);
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), PlaceTime.InZone(instant, Zagreb));
    }

    [Fact]
    public void Wall_time_in_the_autumn_overlap_takes_the_earlier_instant()
    {
        // 02:30 on 2026-10-25 happens twice; the earlier is CEST (+2) = 00:30 UTC.
        Assert.Equal(Utc(2026, 10, 25, 0, 30), PlaceTime.WallToUtc(new DateTime(2026, 10, 25, 2, 30, 0), Zagreb));
    }

    [Fact]
    public void A_skipped_time_moves_forward_by_the_length_of_the_gap_not_by_an_hour()
    {
        // Lord Howe Island's clocks jump half an hour on 2026-10-04 (02:00 to 02:30). 02:15 does not exist there,
        // so it reads as 02:45 local time, which is 15:45 UTC on the day before.
        var instant = PlaceTime.WallToUtc(new DateTime(2026, 10, 4, 2, 15, 0), LordHowe);

        Assert.Equal(Utc(2026, 10, 3, 15, 45), instant);
        Assert.Equal(new DateTime(2026, 10, 4, 2, 45, 0), PlaceTime.InZone(instant, LordHowe));
    }

    [Fact]
    public void An_ambiguous_time_on_a_half_hour_zone_takes_the_earlier_instant()
    {
        // On 2026-04-05 the clocks go from 02:00 (UTC+11) back to 01:30 (UTC+10:30), so 01:45 happens twice.
        // The earlier one is at UTC+11: 14:45 UTC on the day before.
        Assert.Equal(Utc(2026, 4, 4, 14, 45), PlaceTime.WallToUtc(new DateTime(2026, 4, 5, 1, 45, 0), LordHowe));
    }

    [Fact]
    public void A_wall_time_is_read_on_the_wall_whatever_its_kind()
    {
        var wall = new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal(Utc(2026, 10, 24, 21, 30), PlaceTime.WallToUtc(wall, Zagreb));
    }

    [Fact]
    public void Wall_times_around_the_autumn_switch_convert_in_the_same_zone()
    {
        Assert.Equal(Utc(2026, 10, 24, 21, 45), PlaceTime.WallToUtc(new DateTime(2026, 10, 24, 23, 45, 0), Zagreb));
        // 03:10 on the switch day is after the overlap, so it is winter time (+1).
        Assert.Equal(Utc(2026, 10, 25, 2, 10), PlaceTime.WallToUtc(new DateTime(2026, 10, 25, 3, 10, 0), Zagreb));
    }

    [Fact]
    public void A_windows_zone_id_converts_the_same_way_as_its_iana_zone()
    {
        var windowsZone = Zones.Find("Central European Standard Time");

        Assert.NotNull(windowsZone);
        Assert.Equal(Utc(2026, 10, 24, 21, 30), PlaceTime.WallToUtc(new DateTime(2026, 10, 24, 23, 30, 0), windowsZone));
    }

    [Fact]
    public void Wall_to_utc_refuses_a_null_zone()
    {
        Assert.Throws<ArgumentNullException>(() => PlaceTime.WallToUtc(new DateTime(2026, 10, 24, 23, 30, 0), null!));
    }

    [Fact]
    public void A_day_at_the_start_of_the_calendar_throws_instead_of_clamping()
    {
        // 00:00 on 1 January of year 1 in Zagreb is 23:00 on 31 December of year 0. .NET's own conversion would clamp it.
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaceTime.StartOfDayUtc(DateOnly.MinValue, Zagreb));
    }

    // -- days --------------------------------------------------------------------------------------------

    [Fact]
    public void Start_of_a_day_is_its_midnight_on_the_place_clock()
    {
        // Midnight on 2026-10-24 is still CEST (+2), so it is 22:00 UTC on the day before.
        Assert.Equal(Utc(2026, 10, 23, 22, 0), PlaceTime.StartOfDayUtc(new DateOnly(2026, 10, 24), Zagreb));
    }

    [Fact]
    public void End_of_a_day_is_its_last_tick_on_the_place_clock()
    {
        Assert.Equal(Utc(2026, 10, 24, 21, 59, 59, 9_999_999), PlaceTime.EndOfDayUtc(new DateOnly(2026, 10, 24), Zagreb));
    }

    [Fact]
    public void A_day_on_the_autumn_switch_is_25_hours_long()
    {
        var start = PlaceTime.StartOfDayUtc(new DateOnly(2026, 10, 25), Zagreb);
        var end = PlaceTime.EndOfDayUtc(new DateOnly(2026, 10, 25), Zagreb);

        Assert.Equal(Utc(2026, 10, 24, 22, 0), start);
        Assert.Equal(Utc(2026, 10, 25, 22, 59, 59, 9_999_999), end);
        Assert.Equal(TimeSpan.FromHours(25), end.AddTicks(1) - start);
    }

    [Fact]
    public void A_day_on_the_spring_switch_is_23_hours_long()
    {
        var start = PlaceTime.StartOfDayUtc(new DateOnly(2026, 3, 29), Zagreb);
        var end = PlaceTime.EndOfDayUtc(new DateOnly(2026, 3, 29), Zagreb);

        Assert.Equal(Utc(2026, 3, 28, 23, 0), start);
        Assert.Equal(Utc(2026, 3, 29, 21, 59, 59, 9_999_999), end);
        Assert.Equal(TimeSpan.FromHours(23), end.AddTicks(1) - start);
    }

    [Fact]
    public void A_multi_day_span_ends_at_the_end_of_its_last_day()
    {
        Assert.Equal(Utc(2026, 10, 22, 22, 0), PlaceTime.StartOfDayUtc(new DateOnly(2026, 10, 23), Zagreb));
        // 26 October is winter time (+1).
        Assert.Equal(Utc(2026, 10, 26, 22, 59, 59, 9_999_999), PlaceTime.EndOfDayUtc(new DateOnly(2026, 10, 26), Zagreb));
    }

    [Fact]
    public void End_of_the_last_day_of_the_calendar_throws_instead_of_overflowing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaceTime.EndOfDayUtc(DateOnly.MaxValue, Zagreb));
    }

    // -- instant to place ------------------------------------------------------------------------------

    [Theory]
    // Autumn switch, 25 October 2026: local midnight is still CEST, at 22:00 UTC on the 24th.
    [InlineData("2026-10-24T21:59:00Z", 2026, 10, 24)] // 23:59 CEST, 24 October
    [InlineData("2026-10-24T22:00:00Z", 2026, 10, 25)] // 00:00 CEST, 25 October
    [InlineData("2026-10-25T00:30:00Z", 2026, 10, 25)] // 02:30 CEST, before the clocks go back
    [InlineData("2026-10-25T01:30:00Z", 2026, 10, 25)] // 02:30 CET, the repeated hour
    [InlineData("2026-10-25T22:59:00Z", 2026, 10, 25)] // 23:59 CET, 25 October
    [InlineData("2026-10-25T23:00:00Z", 2026, 10, 26)] // 00:00 CET, 26 October
    // Spring switch, 28 March 2027: local midnight is CET, at 23:00 UTC on the 27th.
    [InlineData("2027-03-27T22:59:00Z", 2027, 3, 27)] // 23:59 CET, 27 March
    [InlineData("2027-03-27T23:00:00Z", 2027, 3, 28)] // 00:00 CET, 28 March
    [InlineData("2027-03-28T00:30:00Z", 2027, 3, 28)] // 01:30 CET, before the clocks go forward
    [InlineData("2027-03-28T01:30:00Z", 2027, 3, 28)] // 03:30 CEST, after the switch
    // New year: a winter instant that is already the next day in Zagreb.
    [InlineData("2026-12-31T22:30:00Z", 2026, 12, 31)] // 23:30 CET, 31 December
    [InlineData("2026-12-31T23:30:00Z", 2027, 1, 1)] // 00:30 CET, 1 January
    // Ordinary summer day.
    [InlineData("2026-07-15T22:30:00Z", 2026, 7, 16)] // 00:30 CEST, 16 July
    public void DateOf_names_the_place_day_an_instant_falls_on(string utcInstant, int year, int month, int day)
    {
        var instant = DateTimeOffset.Parse(utcInstant, CultureInfo.InvariantCulture).UtcDateTime;

        Assert.Equal(new DateOnly(year, month, day), PlaceTime.DateOf(instant, Zagreb));
    }

    [Fact]
    public void DateOf_reads_an_unspecified_kind_as_utc()
    {
        var unspecified = new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(new DateOnly(2027, 1, 1), PlaceTime.DateOf(unspecified, Zagreb));
    }

    [Fact]
    public void The_date_at_a_place_is_the_place_calendar_day_not_the_viewers()
    {
        // 22:30 UTC is already the next day in Zagreb (CEST) but not in London (BST, 23:30).
        var now = Utc(2026, 9, 10, 22, 30);

        Assert.Equal(new DateOnly(2026, 9, 11), PlaceTime.DateOf(now, Zagreb));
        Assert.Equal(new DateOnly(2026, 9, 10), PlaceTime.DateOf(now, London));
    }

    [Fact]
    public void In_zone_reading_is_unspecified_kind_even_in_the_utc_zone()
    {
        var reading = PlaceTime.InZone(Utc(2026, 7, 1, 10, 0), TimeZoneInfo.Utc);

        Assert.Equal(DateTimeKind.Unspecified, reading.Kind);
        Assert.Equal(new DateTime(2026, 7, 1, 10, 0, 0), reading);
    }

    [Fact]
    public void In_zone_fails_at_the_top_of_the_calendar_instead_of_clamping()
    {
        // 23:30 UTC on 31 December 9999 is 00:30 on 1 January 10000 in Zagreb, which DateTime cannot hold.
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaceTime.InZone(Utc(9999, 12, 31, 23, 30), Zagreb));
    }

    [Fact]
    public void In_zone_refuses_a_null_zone()
    {
        Assert.Throws<ArgumentNullException>(() => PlaceTime.InZone(Utc(2026, 7, 1, 10, 0), null!));
    }
}
