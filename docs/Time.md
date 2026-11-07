# Ballast.Time: usage guide

Ballast.Time reads time at a place in that place's own zone. It finds a zone from its id, turns a wall-clock time at a place
into a UTC instant (including the hour a spring switch skips and the hour an autumn switch repeats), shows a place's clock
with its offset when that offset differs from the viewer's, and gives a clock for one zone that reads its time from a
`TimeProvider`. It also gives the national public holidays of registered countries (see Public holidays below).

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

## Public holidays

The national public holidays and days off of registered countries, computed from each law's rules. Nothing is fetched and
nothing is cached. A calendar is a list of dates, so no zone is involved: the caller passes the date in the place's own
calendar (see the worked example below).

A country with no registered calendar gives `null`. The library never answers for another country, and never guesses. A
caller that gets `null` refuses or asks.

The examples below use Croatia (`HR`) and assume `var croatia = PublicHolidayCalendar.For("HR")!;`.

### `PublicHolidayCalendar.For(string? countryIso2)`

Returns the national calendar of a country, or `null`.

- The code is matched without regard to case or surrounding white space: `"hr"` and `" HR "` give the same calendar.
- `null`, a blank string, and a code with no registered calendar (`BA`, whose Republika Srpska calendar is not confirmed)
  all give `null`.

```csharp
PublicHolidayCalendar.For("hr");   // the Croatian calendar
PublicHolidayCalendar.For("BA");   // null: no Bosnia and Herzegovina calendar is registered
PublicHolidayCalendar.For("CH");   // null: Swiss holidays are set by each canton
```

Fails: never throws.

### `PublicHolidayCalendar.For(string? countryIso2, string? region)`

Returns the calendar of a country or of one of its regions, or `null`.

- `region` is `null` for the national calendar: `For("HR", null)` is the same as `For("HR")`.
- `region` is matched without regard to case or surrounding white space. A blank region (`""`, `"  "`) is not the national
  calendar: it gives `null`.
- A region the country does not list gives `null`.

```csharp
PublicHolidayCalendar.For("BA", "RS");   // null: the Republika Srpska calendar is not confirmed
PublicHolidayCalendar.For("BA", "FBIH"); // null: the Federation's calendar is not confirmed
PublicHolidayCalendar.For("HR", "X");    // null: Croatia lists no regions
```

Fails: never throws.

### `PublicHolidayCalendar.SupportedCountries`

The ISO codes, in upper case and sorted, of every country with at least one registered calendar. It is read-only and
the same list on every call. Right now it is `AT, CZ, HR, HU, IT, RS, SI`. `BA` is not in it: its only calendar, Republika
Srpska, is not confirmed (see "Not registered").

### `IPublicHolidayCalendar`

Every calendar has these members.

| Member | Meaning |
|---|---|
| `CountryIso2` | The ISO 3166-1 alpha-2 code, in upper case. |
| `Region` | The region code, or `null` for the country's own calendar. |
| `Scope` | One sentence saying what the list holds and what it leaves out. Show it wherever the list is used. |
| `FirstYear` | The first year covered: always `2020`. |
| `LastYear` | The last year covered: always `2100`. |
| `IsHoliday(DateOnly day)` | `true` when the date is a holiday or a day off in this calendar, a day in lieu included. |
| `InYear(int year)` | Every entry of the year, sorted by date. A day in lieu is its own entry with `IsSubstituteDay` set. |

`IsHoliday` and `InYear` throw `ArgumentOutOfRangeException` for a year outside `FirstYear` to `LastYear`. They never return
an empty list or a guess for a year they do not cover. The range is 2020 to 2100 for every calendar, because a holiday law
changes over time, and a rule is only known to hold for the years it was checked against.

Two holidays may fall on the same date (Western and Orthodox Easter can coincide, and a day in lieu never takes a date
already listed, but a fixed holiday and an Easter-based one can still share a date). Both are listed.

### `PublicHoliday`

```csharp
public sealed record PublicHoliday(DateOnly Date, string LocalName, string EnglishName, bool IsSubstituteDay = false);
```

- `Date`: the calendar date. It is a plain date in the country's own calendar, with no zone.
- `LocalName`: the name in the country's language, in its own script. For a day in lieu, it is the holiday's own name.
- `EnglishName`: the English name. A day in lieu gets the suffix ` (day off in lieu)`.
- `IsSubstituteDay`: `true` for a day in lieu, a day off that stands in for a holiday that fell on a Sunday. The original
  Sunday entry stays in the list.

### `Easter.Western(int year)` and `Easter.Orthodox(int year)`

The Western (Gregorian) and Orthodox Easter Sundays, as Gregorian `DateOnly` values. The calendars compute their Easter days
from these. They are public, so an app can use the same dates.

```csharp
Easter.Western(2026);   // 2026-04-05
Easter.Orthodox(2026);  // 2026-04-12
```

Fails: `ArgumentOutOfRangeException` for a year outside 1583 to 9999, the years the Gregorian calendar covers. A year outside
that range is never answered with a guess.

### Calendars

Each registered calendar follows the rules of its law. Where a day is named in the law without a date rule, its offset from
Easter is given in the source note. Every row in a table below is a day the calendar lists; a day that is not listed is
absent, not a day of work by inference.

| Country | Region | Status | Source | Kind of source | Scope (as the calendar states it) |
|---|---|---|---|---|---|
| HR | national | Confirmed, read 2026-11-06 | https://narodne-novine.nn.hr/clanci/sluzbeni/2019_11_110_2212.html (NN 110/2019, Article 1) | Official gazette (Narodne novine) | Public holidays and non-working days of the Croatian holidays law (NN 110/2019, Article 1). Memorial days (spomendani) are not included. |
| SI | national | Confirmed, read 2026-11-06 | https://www.gov.si/teme/drzavni-prazniki-in-dela-prosti-dnevi/ (ZPDPD, Articles 1 and 2) | Government summary page (gov.si), not the statute text | Public holidays and days off of the Slovenian holidays law (ZPDPD). Commemorations that are not days off are not included. |
| AT | national | Confirmed, read 2026-11-06 | https://www.ris.bka.gv.at/GeltendeFassung.wxe?Abfrage=Bundesnormen&Gesetzesnummer=10008541 (Arbeitsruhegesetz, Section 7 Abs. 2) | Official federal legal information system (RIS) | Nationwide public holidays of the Arbeitsruhegesetz, Section 7 Abs. 2. The list has no regional days. |
| IT | national | Confirmed, read 2026-11-06 | https://www.normattiva.it/uri-res/N2Ls?urn:nir:stato:legge:1949-05-27;260~art2 (Law 260/1949, Article 2) | Official legal portal (Normattiva) | National public holidays under Law 260/1949 and its amendments; local patron-saint days and 4 October (Law 151/2025) are not included. |
| CZ | national | Confirmed, read 2026-11-06 | https://www.zakonyprolidi.cz/cs/2000-245 (Act No. 245/2000 Coll., sections 1 to 3) | Legal database reproducing the consolidated law (not the official Collection of Laws) | State holidays and other holidays under Act No. 245/2000 Coll., all of them days of rest. |
| HU | national | Confirmed, read 2026-11-06 | https://net.jogtar.hu/jogszabaly?docid=A1200001.TV (Act I of 2012, section 102 (1)) | Hungarian legal database giving the Act's text | Public holidays under section 102 (1) of Act I of 2012; bridge days swapped by ministerial decree each year are not included. |
| RS | national | Confirmed, read 2026-11-06 | https://www.paragraf.rs/propisi/zakon_o_drzavnim_i_drugim_praznicima_u_republici_srbiji.html (Law on State and Other Holidays, Articles 1, 1a, 2, 3, 3a) | Legal database reproducing the gazette text (not the official Sl. glasnik RS) | National public holidays under the Law on State and Other Holidays: state holidays and the Orthodox Christmas and Easter holidays. Victory Day (9 May, a working day), the working commemorations and the religious holidays of other communities are not included. |

Whether a Sunday holiday (Easter Sunday, Pentecost Sunday) is listed differs by country because the laws differ: Hungary's
section 102 (1) names neither, while some other countries' laws do list them.

Limits that the sources leave open:

- **Serbia (RS)**: the source is a legal database. Amendments after the consolidated text it shows (92/2011) were not read.
- **Czechia (CZ)**: the source is a legal database. The official Collection of Laws was not read.
- **Hungary (HU)**: the bridge days that the government sets by decree each year are not modelled. The footnote marker on
  section 102 (1) was not found in the read, so any amendment note it carries was not read.
- **Italy (IT)**: 4 October (Law 151/2025) is not listed. The text read says it is a festa nazionale but does not state that work stops.

Days that a rule table cannot express are left out and named in the calendar's `Scope`: lunar holidays (Eid al-Fitr, Eid al-Adha), regional and
patron-saint days, and days a region or religious community keeps on its own.

#### Days in lieu (`sundayMovesToNextWorkingDay`)

Serbia (RS) moves a substitutable holiday that falls on a Sunday. The day in lieu is the first
following day that is not a Saturday, not a Sunday and not already listed. In Serbia, 15 February 2026 is a Sunday. 16
February is the second day of the holiday, so the day in lieu is 17 February 2026, and `InYear(2026)` lists it with
`IsSubstituteDay` set and the name `Statehood Day (day off in lieu)`. A Saturday holiday is not moved. The other registered
calendars do not move days.

#### Not registered

These countries have no registered calendar, so `For` gives `null` for them. Each class exists in the code and waits for its
law to be confirmed.

| Country | Why it is not registered |
|---|---|
| CH (Switzerland) | Not supported. Holidays are set by each canton (only 1 August is federal), so a country-level list would mislead. |
| DE (Germany) | Not confirmed against its law. Only 3 October is confirmed (Einigungsvertrag, Article 2). The other days are set by the 16 Länder's laws, which were not read. Nationwide days only, when registered. |
| SK (Slovakia) | Not confirmed against its law. The slov-lex page returned only the Act's title. |
| ME (Montenegro) | Not confirmed. The government page did not open, and the text read is from a legal database that is current only to 2021. |
| MK (North Macedonia) | Not confirmed. The official gazette failed its certificate check, and the Assembly site returned HTTP 403. |
| AL (Albania) | Not confirmed. The official PDF of Law No. 7651 returned HTTP 404; the rows follow a secondary source. Its weekend rule includes Saturdays, which the engine cannot express. |
| XK (Kosovo) | Not confirmed against the official gazette. The text read is a copy of the 2008 gazette. Its Saturday rule is not expressible: the engine moves only Sundays. |
| BA, Republika Srpska (`For("BA", "RS")`) | Not confirmed. Its Sunday-move rule and its believers-only religious days are not confirmed (Constitutional Court decision U 3/13 is on record; the rest of the law's text was read from a legal database, not the official gazette). The class stays in the code, unregistered. |
| BA, state level (`For("BA")`) | Not confirmed. No state law listing the holidays was found; the two proclamation days rest on laws whose text was not read. |
| BA, FBIH (`For("BA", "FBIH")`) | Not confirmed. The federal holiday law was not read; the rows follow news and ministry notices. |

#### Worked example

Today's holiday status in Croatia, from the clock of the place (Zagreb), with the date passed as a plain calendar date:

```csharp
var zagreb = new LocalClock("Europe/Zagreb", TimeProvider.System);
var croatia = PublicHolidayCalendar.For("HR") ?? throw new InvalidOperationException("No calendar for HR.");

bool offToday = croatia.IsHoliday(zagreb.Today); // the date in Zagreb, not in UTC or the phone's zone
```

`zagreb.Today` is the date in Zagreb, which changes at midnight in Zagreb. A holiday check for a place uses the place's own
date. The same check for a viewer in another zone would need the viewer's date and the place's calendar, both passed explicitly.

With a fixed date in 2026 (Western Easter Sunday is 5 April, so Easter Monday is 6 April):

```csharp
croatia.IsHoliday(new DateOnly(2026, 4, 6));   // true: Easter Monday
croatia.IsHoliday(new DateOnly(2026, 12, 25)); // true: Christmas Day
croatia.IsHoliday(new DateOnly(2026, 12, 24)); // false: Christmas Eve is not in the Croatian list
```

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
| `PublicHolidayCalendar.For` (both forms) | Yes: null or blank country, unregistered country, unregistered or blank region | Never |
| `PublicHolidayCalendar.SupportedCountries` | No | Never |
| `IPublicHolidayCalendar.IsHoliday`, `InYear` | No | `ArgumentOutOfRangeException` for a year outside 2020 to 2100; `InvalidOperationException` if a day in lieu would fall outside its year (no table does this) |
| `Easter.Western`, `Easter.Orthodox` | No | `ArgumentOutOfRangeException` for a year outside 1583 to 9999 |

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
- It does not list every holiday of a country. A calendar lists only what its `Scope` says, and a country, region or religious
  community with no registered calendar gives `null`. It does not model bridge days or lunar holidays.
