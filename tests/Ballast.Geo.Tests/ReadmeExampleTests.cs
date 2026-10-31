// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Geo;

namespace Ballast.Geo.Tests;

/// <summary>Runs the example from the package README, verbatim, and checks what it prints.</summary>
public class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_runs_as_written()
    {
        var output = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(output);
        try
        {
            // ----- the README example, verbatim -----
            // A zone drawn as one ring of [longitude, latitude] positions, as GeoJSON writes them.
            var zone = GeoPolygon.TryParseRings("[[15.9,45.7],[16.1,45.7],[16.1,45.9],[15.9,45.9],[15.9,45.7]]")
                ?? throw new InvalidOperationException("The zone is malformed.");

            // The point is given latitude first, then longitude.
            var inside = GeoPolygon.Contains(45.8, 16.0, zone);
            Console.WriteLine($"Inside the zone: {inside}");

            // The straight-line distance between two points, in metres.
            var metres = GeoDistance.HaversineMeters(45.8, 15.98, 43.5, 16.44);
            Console.WriteLine($"Distance: {metres:F0} m");

            // The box a nearby search filters on: every position within 1 km of the centre.
            var box = GeoBox.Around(45.8, 15.98, 1000);
            Console.WriteLine($"Crosses the 180th meridian: {box.CrossesAntimeridian}");
            // ----- end of the example -----
        }
        finally
        {
            Console.SetOut(previous);
        }

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Inside the zone: True", lines[0]);
        Assert.Equal("Distance: 258323 m", lines[1]);
        Assert.Equal("Crosses the 180th meridian: False", lines[2]);
    }
}
