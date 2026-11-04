// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;

namespace Ballast.AppUpdates.Tests;

/// <summary>The configuration shape the apps are set up with, read as raw values.</summary>
public class AppVersionConfigReaderTests
{
    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void The_platform_section_is_read_as_it_is_configured()
    {
        var reader = new AppVersionConfigReader(Config(new()
        {
            ["AppVersion:Android:LatestBuild"] = "12",
            ["AppVersion:Android:MinimumBuild"] = "8",
            ["AppVersion:Android:LatestVersion"] = "0.2.0",
            ["AppVersion:Android:StoreUrl"] = "https://store.example.test/app",
        }));

        var read = reader.Read("Android", null);

        Assert.Equal("Android", read.Label);
        Assert.Null(read.Channel);
        Assert.Equal("12", read.LatestBuild);
        Assert.Equal("8", read.MinimumBuild);
        Assert.Equal("0.2.0", read.LatestVersion);
        Assert.Equal("https://store.example.test/app", read.StoreUrl);
        Assert.Empty(read.Messages);
    }

    [Fact]
    public void A_channel_with_numbers_answers_under_its_own_label()
    {
        var reader = new AppVersionConfigReader(Config(new()
        {
            ["AppVersion:Android:LatestBuild"] = "12",
            ["AppVersion:Android:direct:LatestBuild"] = "14",
            ["AppVersion:Android:direct:StoreUrl"] = "https://storage.example.test/app-14.apk",
        }));

        var read = reader.Read("Android", "direct");

        Assert.Equal("Android:direct", read.Label);
        Assert.Equal("direct", read.Channel);
        Assert.Equal("14", read.LatestBuild);
        Assert.Equal("https://storage.example.test/app-14.apk", read.StoreUrl);
    }

    [Fact]
    public void A_channel_section_with_no_numbers_leaves_the_platform_section_to_answer()
    {
        var reader = new AppVersionConfigReader(Config(new()
        {
            ["AppVersion:Android:LatestBuild"] = "12",
            ["AppVersion:Android:play:StoreUrl"] = "https://play.example.test/app",
        }));

        var read = reader.Read("Android", "play");

        Assert.Equal("Android", read.Label);
        Assert.Null(read.Channel);
        Assert.Equal("12", read.LatestBuild);
        Assert.Null(read.StoreUrl);
    }

    [Fact]
    public void Messages_are_read_by_language_and_blank_ones_are_left_out()
    {
        var reader = new AppVersionConfigReader(Config(new()
        {
            ["AppVersion:Android:LatestBuild"] = "12",
            ["AppVersion:Android:Messages:en"] = "Fixes sign-in.",
            ["AppVersion:Android:Messages:hr"] = "   ",
        }));

        var read = reader.Read("Android", null);

        var message = Assert.Single(read.Messages);
        Assert.Equal("en", message.Key);
        Assert.Equal("Fixes sign-in.", message.Value);
        Assert.Equal("Fixes sign-in.", read.Messages["EN"]);
    }

    [Fact]
    public void A_platform_that_is_not_configured_reads_as_nulls()
    {
        var read = new AppVersionConfigReader(Config(new())).Read("ios", null);

        Assert.Equal("ios", read.Label);
        Assert.Null(read.LatestBuild);
        Assert.Null(read.MinimumBuild);
        Assert.Null(read.StoreUrl);
        Assert.Empty(read.Messages);
    }

    [Fact]
    public void The_section_name_is_read_from_the_options()
    {
        var reader = new AppVersionConfigReader(
            Config(new() { ["Versions:Android:LatestBuild"] = "9" }),
            new AppUpdateOptions { SectionName = "Versions" });

        Assert.Equal("9", reader.Read("Android", null).LatestBuild);
    }

    [Fact]
    public void A_blank_platform_name_is_refused() =>
        Assert.ThrowsAny<ArgumentException>(() => new AppVersionConfigReader(Config(new())).Read(" ", null));

    [Fact]
    public void A_missing_configuration_is_refused() =>
        Assert.Throws<ArgumentNullException>(() => new AppVersionConfigReader(null!));
}
