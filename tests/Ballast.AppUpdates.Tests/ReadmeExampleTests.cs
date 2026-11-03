// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AppUpdates;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.AppUpdates.Tests;

/// <summary>
/// Runs the example in src/Ballast.AppUpdates/README.md exactly as it is written there. The README's using lines are at the top of this
/// file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.AppUpdates/README.md.
        // The build numbers come from configuration. Build 18 is below the minimum of 20, so the answer is that it is required.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppVersion:Android:LatestBuild"] = "24",
                ["AppVersion:Android:MinimumBuild"] = "20",
            })
            .Build();
        var manager = new AppVersionManager(configuration, NullLogger<AppVersionManager>.Instance);
        var answer = manager.Get("android");
        Console.WriteLine(answer.LatestBuild);

        var decision = AppVersionRules.Decide(18, answer, dismissedLatestBuild: null);
        Console.WriteLine(decision);

        Assert.Equal(24, answer.LatestBuild);
        Assert.Equal(AppUpdateDecision.UpdateRequired, decision);
    }
}
