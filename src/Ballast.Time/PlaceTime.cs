// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Time;

/// <summary>
/// Time at a place. A local time is always read in the zone it belongs to, and a clock shows a place's own time with
/// its offset whenever that offset differs from the viewer's.
/// </summary>
/// <remarks>
/// <para>
/// Every function takes the zone as an argument. The one exception is a <see cref="DateTimeKind.Local"/> instant, which
/// <see cref="Instants.AsUtc(DateTime)"/> converts with the machine's zone. So a call gives the same answer on a server, a
/// phone and a test, for every instant that is not Local.
/// </para>
/// <para>
/// A place time that cannot be placed is reported, never guessed. A clock with no place zone shows the viewer's time
/// <em>with the viewer's offset label</em>, so it is never presented as the place's own clock.
/// </para>
/// </remarks>
public static class PlaceTime
{
    /// <summary>
    /// The UTC instant of a wall-clock time at a place. A time the clocks skip moves forward by the gap, and an ambiguous
    /// time takes the earlier instant.
    /// </summary>
    /// <param name="wall">
    /// The clock reading at the place. Its <see cref="DateTime.Kind"/> is not consulted: it is always read as the clock on
    /// the wall in <paramref name="zone"/>.
    /// </param>
    /// <param name="zone">The zone the clock reading is in.</param>
    /// <returns>The instant, with <see cref="DateTimeKind.Utc"/> kind.</returns>
    /// <remarks>
    /// <para>
    /// A time inside the spring-forward gap is read with the offset in force a day earlier, so it moves forward by the
    /// length of the gap: an hour in most zones, half an hour where the gap is half an hour. A time in the autumn overlap
    /// takes the earlier of its two instants, the one with the larger offset.
    /// </para>
    /// <para>
    /// Use this for a time a person typed, or a window bound. A GTFS service time is a different thing and needs its own
    /// rule.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="zone"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The instant falls outside <see cref="DateTime"/>'s range (a wall time at the very start or end of the calendar).
    /// </exception>
    public static DateTime WallToUtc(DateTime wall, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var naive = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);

        // Subtraction throws when the result leaves DateTime's range. TimeZoneInfo.ConvertTimeToUtc would clamp it instead.
        return DateTime.SpecifyKind(naive - OffsetOf(naive, zone), DateTimeKind.Utc);
    }

    /// <summary>
    /// The UTC instant at which a place's day starts (its 00:00 wall clock).
    /// </summary>
    /// <param name="day">The calendar day at the place.</param>
    /// <param name="zone">The zone of the place.</param>
    /// <returns>The first instant of the day, with <see cref="DateTimeKind.Utc"/> kind.</returns>
    /// <remarks>
    /// On a zone that skips midnight, the day starts at the first instant after the gap, as <see cref="WallToUtc"/> gives.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="zone"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The instant falls outside <see cref="DateTime"/>'s range, which happens for the first day of the calendar in a zone
    /// that is ahead of UTC.
    /// </exception>
    public static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone) =>
        WallToUtc(day.ToDateTime(TimeOnly.MinValue), zone);

    /// <summary>
    /// The UTC instant of a place's day's last tick (its 23:59:59.9999999 wall clock). A day the clocks shorten or
    /// lengthen is covered in full: a 23-hour day ends at the same wall-clock tick as any other day.
    /// </summary>
    /// <param name="day">The calendar day at the place.</param>
    /// <param name="zone">The zone of the place.</param>
    /// <returns>The last instant of the day, with <see cref="DateTimeKind.Utc"/> kind.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="zone"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The day is <see cref="DateOnly.MaxValue"/> (the next day is outside <see cref="DateTime"/>'s range), or the instant
    /// falls outside that range.
    /// </exception>
    public static DateTime EndOfDayUtc(DateOnly day, TimeZoneInfo zone) =>
        WallToUtc(day.ToDateTime(TimeOnly.MinValue).AddDays(1).AddTicks(-1), zone);

    /// <summary>
    /// The calendar date at a place on which an instant falls.
    /// </summary>
    /// <param name="instant">The instant. Read as described in <see cref="Instants.AsUtc(DateTime)"/>.</param>
    /// <param name="zone">The zone of the place.</param>
    /// <returns>The date on the place's clock.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="zone"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The instant is so close to the end of <see cref="DateTime"/>'s range that its local reading would be outside it.
    /// </exception>
    public static DateOnly DateOf(DateTime instant, TimeZoneInfo zone) => DateOnly.FromDateTime(InZone(instant, zone));

    /// <summary>
    /// The clock reading at a place for an instant.
    /// </summary>
    /// <param name="instant">The instant. Read as described in <see cref="Instants.AsUtc(DateTime)"/>.</param>
    /// <param name="zone">The zone of the place.</param>
    /// <returns>
    /// The clock reading, with <see cref="DateTimeKind.Unspecified"/> kind: it is a wall-clock time in <paramref name="zone"/>,
    /// not an instant.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="zone"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The local reading would be outside <see cref="DateTime"/>'s range. Unlike <see cref="TimeZoneInfo.ConvertTimeFromUtc"/>,
    /// which clamps it, this fails.
    /// </exception>
    public static DateTime InZone(DateTime instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var utc = Instants.AsUtc(instant);
        return DateTime.SpecifyKind(utc + zone.GetUtcOffset(utc), DateTimeKind.Unspecified);
    }

    /// <summary>
    /// The clock text "HH:mm" of an instant at a place. The place's offset label is added when it differs from the
    /// viewer's, and always when the place's zone is unknown.
    /// </summary>
    /// <param name="instant">The instant. Read as described in <see cref="Instants.AsUtc(DateTime)"/>.</param>
    /// <param name="placeZone">
    /// The zone of the place, or <c>null</c> when it is unknown (for example, <see cref="Zones.Find(string?)"/> gave
    /// <c>null</c>). A null zone shows the time in <paramref name="viewerZone"/>, labelled with the viewer's offset.
    /// </param>
    /// <param name="viewerZone">The zone of the person reading the clock.</param>
    /// <returns>
    /// The time as "HH:mm", such as <c>14:05</c>, or with its offset label, such as <c>14:05 GMT+2</c>.
    /// </returns>
    /// <remarks>
    /// The label is the place's offset at that instant, from <see cref="OffsetLabel(TimeSpan)"/>. The viewer's zone is
    /// compared at the same instant, so the label appears only for a real difference.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="viewerZone"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The local reading would be outside <see cref="DateTime"/>'s range (see <see cref="InZone"/>).
    /// </exception>
    public static string Clock(DateTime instant, TimeZoneInfo? placeZone, TimeZoneInfo viewerZone)
    {
        ArgumentNullException.ThrowIfNull(viewerZone);
        var utc = Instants.AsUtc(instant);
        var shown = placeZone ?? viewerZone;
        var text = InZone(utc, shown).ToString("HH:mm", CultureInfo.InvariantCulture);
        var placeOffset = shown.GetUtcOffset(utc);
        var labelled = placeZone is null || placeOffset != viewerZone.GetUtcOffset(utc);
        return labelled ? $"{text} {OffsetLabel(placeOffset)}" : text;
    }

    /// <summary>
    /// The clock text of a leg that starts and ends at places that may be in different zones: "HH:mm–HH:mm".
    /// </summary>
    /// <param name="start">The start instant.</param>
    /// <param name="startZone">The zone of the start place, or <c>null</c> when it is unknown.</param>
    /// <param name="end">The end instant.</param>
    /// <param name="endZone">The zone of the end place, or <c>null</c> when it is unknown.</param>
    /// <param name="viewerZone">The zone of the person reading the clock.</param>
    /// <returns>
    /// Each end written as <see cref="Clock"/> writes it, joined by an en dash (U+2013), with no spaces around it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewerZone"/> is null.</exception>
    public static string ClockRange(DateTime start, TimeZoneInfo? startZone, DateTime end, TimeZoneInfo? endZone, TimeZoneInfo viewerZone) =>
        $"{Clock(start, startZone, viewerZone)}–{Clock(end, endZone, viewerZone)}";

    /// <summary>
    /// The offset label "GMT+1", "GMT-5" or "GMT+5:30" for a UTC offset.
    /// </summary>
    /// <param name="offset">The offset from UTC. Its seconds, if any, are not shown.</param>
    /// <returns>
    /// <c>GMT</c>, a sign (<c>+</c> for zero and above, <c>-</c> below zero), the hours, and the minutes after a colon
    /// when they are not zero. Zero gives <c>GMT+0</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="offset"/> is 24 hours or more either way. No zone has such an offset, so the value is refused.
    /// </exception>
    public static string OffsetLabel(TimeSpan offset)
    {
        if (offset <= -TimeSpan.FromHours(24) || offset >= TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "A UTC offset must be less than 24 hours either way.");

        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset < TimeSpan.Zero ? -offset : offset;
        var hours = magnitude.Hours.ToString(CultureInfo.InvariantCulture);
        return magnitude.Minutes == 0
            ? $"GMT{sign}{hours}"
            : $"GMT{sign}{hours}:{magnitude.Minutes.ToString("00", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The offset in force at a wall-clock reading in a zone, as the offset that turns the reading into UTC.
    /// </summary>
    private static TimeSpan OffsetOf(DateTime naive, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(naive))
            return zone.GetUtcOffset(naive.AddDays(-1));
        if (zone.IsAmbiguousTime(naive))
            return zone.GetAmbiguousTimeOffsets(naive).Max();
        return zone.GetUtcOffset(naive);
    }
}
