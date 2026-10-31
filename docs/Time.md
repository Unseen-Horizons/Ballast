# Ballast.Time: usage guide

Ballast.Time reads time at a place in that place's own zone. It finds a zone from its id, turns a wall-clock time at a place
into a UTC instant (including the hour a spring switch skips and the hour an autumn switch repeats), shows a place's clock
with its offset when that offset differs from the viewer's, and gives a clock for one zone that reads its time from a
`TimeProvider`.

Install with `dotnet add package Ballast.Time --prerelease`. The package targets `net10.0` and depends on no package.

The examples below use Europe/Zagreb, Europe/London and Europe/Berlin, with dates in 2026, and they assume these variables:

```csharp
var zagreb = Zones.Find("Europe/Zagreb")!;
var london = Zones.Find("Europe/London")!;
var berlin = Zones.Find("Europe/Berlin")!;
var lordHowe = Zones.Find("Australia/Lord_Howe")!;
```

Every output shown was produced by the library.

## The rules it keeps

- **The zone is an argument.** Every function that needs a zone takes it. The one exception is `Instants.AsUtc(DateTime)` for
  a `DateTimeKind.Local` value. A Local value is the machine's own representation of an instant, so the machine's conversion
  is the right one for it (see Instants below).
- **Instants are UTC, and the type says so.** A `DateTime` in the `Utc` kind is an instant. Places and viewers are given as
  zones, never as instants.
- **A zone that is not known is `null`, never a guess.** The library never falls back to UTC, to the machine's zone or to
  another zone.
- **A failure is an exception, never a clamped value.** .NET's own conversions (`TimeZoneInfo.ConvertTimeToUtc`,
  `ConvertTimeFromUtc`) clamp an instant that falls outside `DateTime`'s range to its end, without a sign. This library does
  the offset arithmetic itself, so such a value throws `ArgumentOutOfRangeException`.

## Zones

### `Zones.Find(string? zoneId)`

Returns the `TimeZoneInfo` that `zoneId` names, or `null`.

`zoneId` may be an IANA id (`Europe/Zagreb`) or a Windows id (`Central European Standard Time`). Surrounding whitespace is
removed first. The id must then have this shape: a letter first, then letters, digits, and `_ + - / ( ) .` or spaces, at
most 64 characters in all, with no `..`. The shape is checked before the id is looked up, so an id is never used as a path.

| Input | Result |
|---|---|
| `"Europe/Zagreb"`, `"  Europe/Zagreb\r\n"` | The zone |
| `"Central European Standard Time"` | The zone for that Windows id |
| `null`, `""`, `"   "` | `null` |
| `"Mars/Olympus_Mons"`, `"Not/AZone"` | `null`: the machine does not know the id |
| `"../../etc/passwd"`, `"Europe/../Zagreb"`, `"/Europe/Zagreb"`, `"Europe\Zagreb"` | `null`: not shaped like a zone id |
| `"Ünknown/Zone"`, `"Europe/Zagreb;Europe/London"`, a string with a NUL character | `null` |

```csharp
var zagreb = Zones.Find("Europe/Zagreb");              // the zone
var missing = Zones.Find("Not/AZone");                 // null
var windows = Zones.Find("Central European Standard Time"); // a zone, whose Id is the Windows id
```

Fails: never throws. A bad id is `null`, and the caller reports it. A zone found through a Windows id keeps that id in its
`Id` property. Use `Canonical` to get the IANA id.

Only a lookup is done. Nothing is cached, so a stream of unknown ids cannot make memory grow.

### `Zones.Canonical(string? zoneId)`

Returns the IANA id to store for the zone that `zoneId` names, or `null`.

- An IANA id is returned as written, without surrounding whitespace.
- A Windows id is converted to the IANA id this machine maps it to.
- Anything that `Find` does not find is `null`. Unlike a plain conversion, an unknown id is not kept as written, so a stored
  zone is always one this machine knows.

```csharp
Zones.Canonical("  Europe/Zagreb ");                 // "Europe/Zagreb"
Zones.Canonical("Central European Standard Time");  // "Europe/Warsaw" on this Windows machine
Zones.Canonical("W. Europe Standard Time");         // "Europe/Berlin" on this Windows machine
Zones.Canonical("Mars/Olympus_Mons");               // null
Zones.Canonical(null);                              // null
```

The IANA city chosen for a Windows id is the platform's choice. Those zones share their rules, so the times they give are the
same. The city can differ between machines.

## Instants

### `Instants.AsUtc(DateTime value)`

Returns `value` as a UTC instant, by its kind:

| `DateTimeKind` | Result |
|---|---|
| `Utc` | The value, unchanged |
| `Local` | Converted back to UTC, through `DateTime.ToUniversalTime()`, the exact inverse of the conversion that made the value |
| `Unspecified` | The same clock reading, marked as UTC. Nothing is moved |

Use the kinds this way:

- An `Unspecified` value is right only where the field is documented as UTC (a name ending in `Utc`, or a `Z` suffix).
- A wall-clock time with a zone is not an instant. It needs `PlaceTime.WallToUtc`.
- A `Local` value is the machine's representation of an instant, made by .NET: a JSON reader makes one from a time with an
  offset. `ToUniversalTime` is its exact inverse. The library never creates a Local value, and every value it returns is
  Utc, Unspecified or text. A value in the repeated hour of an autumn switch cannot say which of its two instants it came
  from, so keep the UTC instant wherever you have it.
- A wall-clock time for a place (a time a person typed, a window bound) is converted with `PlaceTime.WallToUtc`, with the
  place's zone. It is never read as a Local value.

```csharp
Instants.AsUtc(new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Utc));          // 12:05 UTC, unchanged
Instants.AsUtc(new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Unspecified));  // 12:05 UTC
```

Fails: no exception for these kinds. For a `Local` value, `DateTime.ToUniversalTime()` clamps a result at the very ends of
`DateTime`'s range instead of failing. Only a value within about a day of either end of the range can be affected.

## Place time

`PlaceTime` is a static class. Every method takes the zone as an argument.

### `PlaceTime.WallToUtc(DateTime wall, TimeZoneInfo zone)`

Returns the UTC instant of a wall-clock time at a place. Use it for a time a person typed, or for a window bound.

| The wall time is | Result |
|---|---|
| Ordinary | Its instant in `zone` |
| In the spring gap (the clocks skip it) | Moves forward by the length of the gap. In Zagreb, 02:30 on 2026-03-29 is 03:30 summer time, 01:30 UTC. Lord Howe Island's half-hour gap moves a time by half an hour |
| In the autumn overlap (the clocks repeat it) | Takes the earlier instant, the one with the larger offset. In Zagreb, 02:30 on 2026-10-25 is 00:30 UTC |

```csharp
PlaceTime.WallToUtc(new DateTime(2026, 10, 24, 23, 30, 0), zagreb); // 2026-10-24T21:30:00Z
PlaceTime.WallToUtc(new DateTime(2026, 3, 29, 2, 30, 0), zagreb);   // 2026-03-29T01:30:00Z (the gap)
PlaceTime.WallToUtc(new DateTime(2026, 10, 25, 2, 30, 0), zagreb);  // 2026-10-25T00:30:00Z (the overlap)
```

The result's `Kind` is `Utc`. The `Kind` of `wall` is not consulted: its clock reading is read as the clock on the wall in
`zone`.

GTFS service times are a different rule: a service day's seconds run from noon minus twelve hours. Keep that rule with the
transit data, not in this method.

Fails:
- `ArgumentNullException`: `zone` is null.
- `ArgumentOutOfRangeException`: the instant falls outside `DateTime`'s range. That happens for a wall time at the very start
  or end of the calendar, in a zone whose offset would move it past the end.

### `PlaceTime.StartOfDayUtc(DateOnly day, TimeZoneInfo zone)`

Returns the first instant of a place's day, its 00:00 on the place's clock.

```csharp
PlaceTime.StartOfDayUtc(new DateOnly(2026, 10, 24), zagreb); // 2026-10-23T22:00:00Z: still summer time
```

On a zone that skips midnight, the day starts at the first instant after the gap, as `WallToUtc` gives.

Fails:
- `ArgumentNullException`: `zone` is null.
- `ArgumentOutOfRangeException`: the instant is outside `DateTime`'s range. For `DateOnly.MinValue` in a zone ahead of UTC,
  this happens.

### `PlaceTime.EndOfDayUtc(DateOnly day, TimeZoneInfo zone)`

Returns the last instant of a place's day: its 23:59:59.9999999 on the place's clock. Together with `StartOfDayUtc` it covers
the whole day, including the 25-hour day of an autumn switch and the 23-hour day of a spring switch.

```csharp
PlaceTime.EndOfDayUtc(new DateOnly(2026, 10, 24), zagreb); // 2026-10-24T21:59:59.9999999Z
var start = PlaceTime.StartOfDayUtc(new DateOnly(2026, 10, 25), zagreb); // 2026-10-24T22:00:00Z
var end = PlaceTime.EndOfDayUtc(new DateOnly(2026, 10, 25), zagreb);     // 2026-10-25T22:59:59.9999999Z
// end.AddTicks(1) - start is 25 hours.
```

Fails:
- `ArgumentNullException`: `zone` is null.
- `ArgumentOutOfRangeException`: the day is `DateOnly.MaxValue`, whose next day is outside `DateTime`'s range, or the instant
  falls outside that range.

### `PlaceTime.DateOf(DateTime instant, TimeZoneInfo zone)`

Returns the calendar date at a place on which an instant falls. A place's date changes at its own midnight, not at midnight
UTC.

```csharp
PlaceTime.DateOf(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc), zagreb); // 2026-10-25: 00:00 in Zagreb
PlaceTime.DateOf(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc), london); // 2026-10-24: 23:00 in London
```

`instant` is read as described in `Instants.AsUtc(DateTime)`. An `Unspecified` value is read as UTC.

Fails:
- `ArgumentNullException`: `zone` is null.
- `ArgumentOutOfRangeException`: the local reading is outside `DateTime`'s range (see `InZone`).

### `PlaceTime.InZone(DateTime instant, TimeZoneInfo zone)`

Returns an instant's clock reading in `zone`.

```csharp
PlaceTime.InZone(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc), london); // 2026-10-24T23:00:00 (Kind Unspecified)
```

The result's `Kind` is always `Unspecified`, even for `TimeZoneInfo.Utc`. It is a wall-clock reading in `zone`, not an
instant.

Fails:
- `ArgumentNullException`: `zone` is null.
- `ArgumentOutOfRangeException`: the reading is outside `DateTime`'s range. For example, 23:30 UTC on 31 December 9999 in
  Zagreb. .NET's `ConvertTimeFromUtc` would clamp it to 23:59:59 without a sign; this method throws.

### `PlaceTime.Clock(DateTime instant, TimeZoneInfo? placeZone, TimeZoneInfo viewerZone)`

Returns the time at a place as text: `HH:mm`, with the place's offset label added when that offset differs from the viewer's.

| `placeZone` | Offset compared with the viewer's | Result |
|---|---|---|
| A zone | Same | `14:05`: the place's time, no label |
| A zone | Different | `14:05 GMT+2`: the place's time, labelled |
| `null` (unknown) | not compared | The viewer's time, labelled with the viewer's offset. It is never presented as the place's clock |

```csharp
var departure = new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Utc);
PlaceTime.Clock(departure, zagreb, london); // "14:05 GMT+2": Zagreb is UTC+2 in September, London UTC+1
PlaceTime.Clock(departure, zagreb, berlin); // "14:05": Berlin has Zagreb's offsets, so no label
PlaceTime.Clock(departure, null, london);   // "13:05 GMT+1": the viewer's time, labelled
```

The label is the place's offset at that instant, from `OffsetLabel`. Both zones are compared at the same instant, so the label
appears only for a real difference.

Fails:
- `ArgumentNullException`: `viewerZone` is null. A null `placeZone` is not a failure: it is the unknown-zone case above.
- `ArgumentOutOfRangeException`: the local reading is outside `DateTime`'s range (see `InZone`).

### `PlaceTime.ClockRange(DateTime start, TimeZoneInfo? startZone, DateTime end, TimeZoneInfo? endZone, TimeZoneInfo viewerZone)`

Returns the two ends of a leg as `HH:mm–HH:mm`. Each end is written as `Clock` writes it, and the two are joined by an en dash
(U+2013), with no spaces around it.

```csharp
var start = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
var end = new DateTime(2026, 9, 10, 14, 0, 0, DateTimeKind.Utc);
PlaceTime.ClockRange(start, zagreb, end, london, zagreb); // "14:00–15:00 GMT+1"
PlaceTime.ClockRange(start, zagreb, end, null, london);   // "14:00 GMT+2–15:00 GMT+1"
```

Fails:
- `ArgumentNullException`: `viewerZone` is null.
- `ArgumentOutOfRangeException`: a local reading is outside `DateTime`'s range.

### `PlaceTime.OffsetLabel(TimeSpan offset)`

Returns an offset as text: `GMT`, a sign, the hours, and the minutes after a colon when they are not zero.

| Offset | Label |
|---|---|
| +1 h | `GMT+1` |
| −5 h | `GMT-5` |
| +5 h 30 min | `GMT+5:30` |
| −3 h 30 min | `GMT-3:30` |
| zero | `GMT+0` |
| +1 h 0 min 30 s | `GMT+1`: a seconds part is dropped |

Fails:
- `ArgumentOutOfRangeException`: the offset is 24 hours or more either way. No zone has such an offset, so it is refused.

## Local clock

### `new LocalClock(string zoneId, TimeProvider time)`

Creates a clock for one zone. The zone is a setting of the caller (a business zone, for example), so it is named once.

```csharp
var clock = new LocalClock("Europe/Zagreb", TimeProvider.System); // in an app
```

A test sets the time with a `TimeProvider` of its own:

```csharp
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

var clock = new LocalClock("Europe/Zagreb", new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero)));
Console.WriteLine(clock.Today); // 2026-10-25: 22:00 UTC is already 00:00 in Zagreb
```

Fails at construction, so a bad setting stops the caller at startup:
- `ArgumentNullException`: `zoneId` or `time` is null.
- `ArgumentException` with `ParamName` `"zoneId"`: `zoneId` is blank, is not shaped like a zone id, or is not a zone this
  machine knows. The message names the id.

### `Zone`

The `TimeZoneInfo` this clock reads. It is the zone that `Zones.Find` gave for `zoneId`, and its `Id` may be the Windows id that
was given.

### `Now`

The current wall-clock time in `Zone`, read from the time provider on each call.

Its `Kind` is `Unspecified`: it is a reading in `Zone`, not an instant. On 2026-03-29 the reading goes from 01:59 straight to
03:00 when the instant reaches 01:00 UTC. On 2026-10-25 the readings from 02:00 to 02:59 occur twice, once at 00:00 to 00:59 UTC and once at 01:00 to
01:59 UTC.

### `Today`

The current calendar date in `Zone`. It changes at midnight in `Zone`, not at midnight UTC.

### `DateOf(DateTime instant)`

The calendar date in `Zone` on which `instant` falls.

```csharp
clock.DateOf(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc)); // 2026-10-25
```

Fails:
- `ArgumentException` with `ParamName` `"instant"`: `instant` has `DateTimeKind.Local`. The machine's zone must never decide
  the date for a zone that is not the machine's. Pass a UTC value: a Local value is the machine's own representation of an
  instant, and this clock never reads the machine's zone.
- `ArgumentOutOfRangeException`: the local reading is outside `DateTime`'s range.

### `ToUtc(DateOnly date, int hour)`

The UTC instant at which `Zone` shows `hour`:00 on `date`.

```csharp
clock.ToUtc(new DateOnly(2026, 3, 29), 2); // 2026-03-29T01:00:00Z: 02:00 does not exist; the first instant after the gap
clock.ToUtc(new DateOnly(2026, 10, 25), 2); // 2026-10-25T00:00:00Z: the first of the two 02:00s
clock.ToUtc(new DateOnly(2026, 7, 15), 9); // 2026-07-15T07:00:00Z
```

The hour is read as the start of its slot. On a repeated hour the slot starts at its first occurrence, and on a skipped hour it
starts at the first instant after the gap.

Fails:
- `ArgumentOutOfRangeException` with `ParamName` `"hour"`: `hour` is less than 0 or more than 23.
- `ArgumentOutOfRangeException`: the instant is outside `DateTime`'s range.

### `IsPast(DateOnly date, TimeOnly time)`

Returns `true` when a slot that starts at `date` and `time` (in `Zone`) is not after `Now`, to the second.

```csharp
// At 12:20 Zagreb time on 2026-07-15:
clock.IsPast(new DateOnly(2026, 7, 15), new TimeOnly(12, 0));   // true: the running hour counts as past
clock.IsPast(new DateOnly(2026, 7, 15), new TimeOnly(13, 0));   // false
clock.IsPast(new DateOnly(2026, 7, 14), new TimeOnly(23, 0));   // true
```

The comparison is on the wall clock. During the repeated hour of an autumn switch the wall clock shows the same reading twice,
so a slot in that hour counts as past for both of its occurrences.

Fails: no exception of its own. Reading `Now` can throw `ArgumentOutOfRangeException` at the very end of `DateTime`'s range.

## Failure summary

| Member | Returns `null` | Throws |
|---|---|---|
| `Zones.Find` | Yes: blank, malformed, unknown | Never |
| `Zones.Canonical` | Yes: anything `Find` does not find | Never |
| `Instants.AsUtc(DateTime)` | No | Never for Utc or Unspecified. A Local value near the ends of the range is clamped by .NET |
| `PlaceTime.WallToUtc`, `StartOfDayUtc`, `EndOfDayUtc` | No | `ArgumentNullException`; `ArgumentOutOfRangeException` at the ends of the range; `EndOfDayUtc` on `DateOnly.MaxValue` |
| `PlaceTime.DateOf`, `InZone` | No | `ArgumentNullException`; `ArgumentOutOfRangeException` at the ends of the range |
| `PlaceTime.Clock`, `ClockRange` | No | `ArgumentNullException` for the viewer's zone; `ArgumentOutOfRangeException` at the ends of the range |
| `PlaceTime.OffsetLabel` | No | `ArgumentOutOfRangeException` for 24 hours or more either way |
| `new LocalClock` | No: the constructor throws instead | `ArgumentNullException`; `ArgumentException` for a blank, malformed or unknown zone |
| `LocalClock.DateOf` | No | `ArgumentException` for a `Local` value; `ArgumentOutOfRangeException` at the ends of the range |
| `LocalClock.ToUtc` | No | `ArgumentOutOfRangeException` for an hour outside 0 to 23, or outside the range |

## Which zone to pass

- **A place's time** (a departure, a leg's end, a timetable's last day): the zone of that place. Pass `null` when the zone is
  unknown, so the time is shown with the viewer's label and is not presented as the place's clock.
- **An event's time** (a poll, a check, "monitoring stopped"): the viewer's zone. The library has no notion of the viewer's
  zone, so the caller passes it.
- **A zone from data** (a feed, a stored id): pass the result of `Zones.Find`, and report `null` rather than substituting a zone.
- **A zone chosen by a setting** (a business zone): construct a `LocalClock`, which refuses an unknown zone at startup.

## What it does not do

- It does not read the machine's zone, except in `Instants.AsUtc(DateTime)` for a `Local` value, as described above.
- It does not convert a wall-clock time from one zone straight to another. Turn it into an instant with `WallToUtc`, then read
  that instant in the other zone with `InZone`.
- It does not model GTFS service days or transit timetables. Those belong to the caller.
- It does not decide which zone a person is in. The caller gives the viewer's zone.
