// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Ballast.Time;

namespace Ballast.Time.Tests;

/// <summary>
/// Runs the example in src/Ballast.Time/README.md as the README gives it, and checks the values its comments state.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_compiles_and_runs()
    {
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
    }

    [Fact]
    public void The_values_the_readme_comments_state_are_the_ones_the_library_gives()
    {
        var zagreb = Zones.Find("Europe/Zagreb") ?? throw new InvalidOperationException("Unknown zone.");
        var london = Zones.Find("Europe/London") ?? throw new InvalidOperationException("Unknown zone.");
        var departure = new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Utc);

        Assert.Equal("14:05 GMT+2", PlaceTime.Clock(departure, zagreb, london));

        var typed = PlaceTime.WallToUtc(new DateTime(2026, 3, 29, 2, 30, 0), zagreb);
        Assert.Equal("2026-03-29 01:30:00Z", typed.ToString("u", CultureInfo.InvariantCulture));
    }
}
