# Ballast.Time

Time at a place, read in that place's own zone, and never in the machine's zone.

- **Zones.** Finds a zone from an IANA id (`Europe/Zagreb`) or a Windows id (`Central European Standard Time`). An id it
  does not know gives `null`. It never guesses.
- **Place time.** Turns a wall-clock time at a place into a UTC instant, handling the hour a spring switch skips and the
  hour an autumn switch repeats. Shows a place's clock with its offset label (`14:05 GMT+2`) when that offset differs
  from the viewer's.
- **Local clock.** A clock for one zone, read from a `TimeProvider`, so a test can set the time.
- **Public holidays.** The national holidays and days off of registered countries, from 2020 to 2100, computed from each
  law's rules (fixed dates, Easter offsets, the day in lieu for a Sunday holiday). A country with no registered calendar
  gives `null`. Nothing is fetched.

A function that needs a zone takes it as an argument, so a call gives the same answer on a server, on a phone and in a test.

Preview: version 0.1.0-preview.4. The usage guide, with every option and every way the library fails, is `docs/Time.md`
in the repository.

## Install

```
dotnet add package Ballast.Time --prerelease
```

## Example

```csharp
using System.Globalization;
using Ballast.Time;

var zagreb = Zones.Find("Europe/Zagreb") ?? throw new InvalidOperationException("Unknown zone.");
var london = Zones.Find("Europe/London") ?? throw new InvalidOperationException("Unknown zone.");

// 12:05 UTC is 14:05 in Zagreb. The viewer is in London, so the place's offset is shown.
var departure = new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Utc);
Console.WriteLine(PlaceTime.Clock(departure, zagreb, london)); // 14:05 GMT+2

// 02:30 does not exist in Zagreb on the spring switch day, so it moves forward to 03:30 local (01:30 UTC).
var typed = PlaceTime.WallToUtc(new DateTime(2026, 3, 29, 2, 30, 0), zagreb);
Console.WriteLine(typed.ToString("u", CultureInfo.InvariantCulture)); // 2026-03-29 01:30:00Z

// A clock for one zone, read from the system clock here.
var clock = new LocalClock("Europe/Zagreb", TimeProvider.System);
Console.WriteLine(clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); // today's date in Zagreb

// Croatia's national holidays on today's date in Zagreb. The date has no zone: it is the calendar date in Croatia.
var croatia = PublicHolidayCalendar.For("HR") ?? throw new InvalidOperationException("No calendar for HR.");
Console.WriteLine(croatia.IsHoliday(clock.Today)); // True on a Croatian holiday, otherwise False
```
