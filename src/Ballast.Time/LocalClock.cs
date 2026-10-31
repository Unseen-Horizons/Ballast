// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time;

/// <summary>
/// A clock for one zone: the wall-clock time and the calendar date in that zone, read from a <see cref="TimeProvider"/>.
/// The zone is a setting of the caller (a business zone, for example), so it is named once, at construction.
/// </summary>
/// <remarks>
/// <para>
/// A method that takes an instant refuses a value of <see cref="DateTimeKind.Local"/>. The machine's zone must never
/// decide what "now" or "today" means for a zone that is not the machine's. An Unspecified value is read as UTC.
/// </para>
/// <para>
/// The clock reads time only from its <see cref="TimeProvider"/>, so a test can set the time with a fake provider.
/// </para>
/// </remarks>
public sealed class LocalClock
{
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates a clock for a zone.
    /// </summary>
    /// <param name="zoneId">An IANA id or a Windows id, as <see cref="Zones.Find(string?)"/> accepts.</param>
    /// <param name="time">Where the current time comes from. <see cref="TimeProvider.System"/> in an app.</param>
    /// <exception cref="ArgumentNullException"><paramref name="zoneId"/> or <paramref name="time"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="zoneId"/> is blank, malformed, or not a zone this machine knows. A bad setting stops the caller at
    /// startup, rather than giving a wrong time later.
    /// </exception>
    public LocalClock(string zoneId, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(zoneId);
        ArgumentNullException.ThrowIfNull(time);
        Zone = Zones.Find(zoneId)
            ?? throw new ArgumentException($"The time zone '{zoneId}' is not known on this machine.", nameof(zoneId));
        _time = time;
    }

    /// <summary>The zone this clock reads.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>
    /// The current wall-clock time in <see cref="Zone"/>, read from the time provider at each call.
    /// </summary>
    /// <remarks>
    /// Its <see cref="DateTimeKind"/> is Unspecified. It is a wall-clock reading in <see cref="Zone"/>, not an instant.
    /// </remarks>
    public DateTime Now => PlaceTime.InZone(_time.GetUtcNow().UtcDateTime, Zone);

    /// <summary>
    /// The current calendar date in <see cref="Zone"/>. It changes at midnight in that zone, not at midnight UTC.
    /// </summary>
    public DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>
    /// The calendar date in <see cref="Zone"/> on which an instant falls.
    /// </summary>
    /// <param name="instant">A UTC instant. Kind Utc or Unspecified (read as UTC). Kind Local is refused.</param>
    /// <returns>The date on the clock's own calendar.</returns>
    /// <exception cref="ArgumentException"><paramref name="instant"/> has <see cref="DateTimeKind.Local"/> kind.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The instant is so close to the end of <see cref="DateTime"/>'s range that its local reading would be outside it.
    /// </exception>
    public DateOnly DateOf(DateTime instant)
    {
        if (instant.Kind == DateTimeKind.Local)
            throw new ArgumentException("A UTC instant is required, not a local time.", nameof(instant));
        return PlaceTime.DateOf(instant, Zone);
    }

    /// <summary>
    /// The UTC instant at which the clock's zone shows <paramref name="hour"/>:00 on <paramref name="date"/>.
    /// </summary>
    /// <param name="date">The calendar date in <see cref="Zone"/>.</param>
    /// <param name="hour">The hour of the day, from 0 to 23.</param>
    /// <returns>The instant, with <see cref="DateTimeKind.Utc"/> kind.</returns>
    /// <remarks>
    /// <para>
    /// On a day the clocks skip the hour, an hour that does not exist gives the first instant after the gap. In Zagreb,
    /// 02:00 on the spring switch day is 01:00 UTC, which is 03:00 local time.
    /// </para>
    /// <para>
    /// On a day the clocks repeat the hour, the slot starts at its first occurrence, the earlier instant. In Zagreb,
    /// 02:00 on the autumn switch day is 00:00 UTC.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hour"/> is less than 0 or more than 23.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The instant falls outside <see cref="DateTime"/>'s range.</exception>
    public DateTime ToUtc(DateOnly date, int hour)
    {
        if (hour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "The hour must be between 0 and 23.");
        return PlaceTime.WallToUtc(date.ToDateTime(new TimeOnly(hour, 0)), Zone);
    }

    /// <summary>
    /// Whether a slot that starts at <paramref name="date"/> and <paramref name="time"/> has passed on the clock's zone.
    /// </summary>
    /// <param name="date">The calendar date of the slot, in <see cref="Zone"/>.</param>
    /// <param name="time">The time the slot starts, in <see cref="Zone"/>.</param>
    /// <returns>
    /// <c>true</c> when the slot's start is not after <see cref="Now"/>, to the second. With whole-hour slots, the running
    /// hour counts as past: 12:00 is past at 12:20.
    /// </returns>
    /// <remarks>
    /// The comparison is on the wall clock. During the repeated hour of an autumn switch the wall clock shows the same
    /// reading twice, so a slot in that hour is past for both of its occurrences.
    /// </remarks>
    public bool IsPast(DateOnly date, TimeOnly time) => date.ToDateTime(time) <= Now;
}
