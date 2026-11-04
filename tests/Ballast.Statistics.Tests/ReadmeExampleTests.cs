// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Statistics;

namespace Ballast.Statistics.Tests;

/// <summary>
/// Runs the example in src/Ballast.Statistics/README.md as the README gives it, and checks the values its comments state.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_compiles_and_runs()
    {
        Console.WriteLine(Math.Round(Stats.NormalUpperTail(1.96), 4));           // 0.025
        var z = Stats.TwoProportionZ(30, 100, 20, 100);
        Console.WriteLine(z is null ? "undefined" : Math.Round(z.Value, 3));    // 1.633
        var adjusted = Stats.BenjaminiHochberg([0.01, 0.04, 0.03, 0.005], 0.05);
        Console.WriteLine(string.Join(" ", adjusted.Select(p => Math.Round(p, 2)))); // 0.02 0.04 0.04 0.02
        Console.WriteLine(Stats.Median([3.0, 1.0, 2.0]));                       // 2
        Console.WriteLine(Stats.Mad([1.0, 2.0, 3.0, 4.0, 100.0]));              // 1
    }

    [Fact]
    public void The_values_the_readme_comments_state_are_the_ones_the_library_gives()
    {
        Assert.Equal(0.025, Math.Round(Stats.NormalUpperTail(1.96), 4));
        Assert.Equal(1.633, Math.Round(Stats.TwoProportionZ(30, 100, 20, 100)!.Value, 3));
        var adjusted = Stats.BenjaminiHochberg([0.01, 0.04, 0.03, 0.005], 0.05);
        double[] expected = [0.02, 0.04, 0.04, 0.02];
        Assert.Equal(expected, adjusted.Select(p => Math.Round(p, 2)).ToArray());
        Assert.Equal(2.0, Stats.Median([3.0, 1.0, 2.0]));
        Assert.Equal(1.0, Stats.Mad([1.0, 2.0, 3.0, 4.0, 100.0]));
    }
}
