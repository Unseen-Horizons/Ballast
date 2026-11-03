// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

using Ballast.Maui.Ads;
using Ballast.Maui.Diagnostics;
using Ballast.Maui.Permissions;

namespace Ballast.Maui.Tests;

/// <summary>Runs the README's example exactly as the README prints it, and checks what it writes.</summary>
public sealed class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_prints_what_the_readme_says()
    {
        var output = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(output);
        try
        {
            RunExample();
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Equal(
            "True\nca-app-pub-1234567890123456/1234567890\nOpenSettings\nSign-in failed for [email] at [link: api.example.com]\n",
            output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>The README's example, as written there.</summary>
    private static void RunExample()
    {
        var document = JsonDocument.Parse("{\"Ads\":{\"Enabled\":true,\"AndroidBannerAdUnitId\":\"ca-app-pub-1234567890123456/1234567890\"}}");
        var ads = AdsOptions.Read(document) with { PlatformSupportsAds = true, IsDebugBuild = false };
        Console.WriteLine(ads.CanInitialize);
        Console.WriteLine(ads.EffectiveBannerAdUnitId);

        Console.WriteLine(LocationAskPolicy.Decide(allowed: false, showRationale: false, requestedOnThisInstall: true));

        Console.WriteLine(CrashReportText.Scrub("Sign-in failed for ana@example.com at https://api.example.com/v1/login"));
    }
}
