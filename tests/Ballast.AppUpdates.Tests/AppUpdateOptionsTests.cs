// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.AppUpdates.Tests;

/// <summary>The defaults the apps ship with, and the settings that are refused.</summary>
public class AppUpdateOptionsTests
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    [Fact]
    public void The_defaults_are_the_wire_names_the_apps_ship_with()
    {
        var options = new AppUpdateOptions();

        Assert.Equal("AppVersion", options.SectionName);
        Assert.Equal("app/version", options.Route);
        Assert.Equal("play", options.StoreChannel);
        Assert.Equal("APP_PLATFORM_UNKNOWN", options.PlatformUnknownCode);
        Assert.Equal("APP_CHANNEL_UNKNOWN", options.ChannelUnknownCode);
        Assert.Equal("APP_VERSION_NOT_CONFIGURED", options.NotConfiguredCode);
        Assert.Equal("APP_VERSION_MISCONFIGURED", options.MisconfiguredCode);
    }

    [Fact]
    public void The_default_platforms_are_android_ios_and_windows()
    {
        var platforms = new AppUpdateOptions().Platforms;

        string[] expected = ["android", "ios", "windows"];
        Assert.Equal(expected, platforms.Keys.ToArray());
        Assert.Equal("Android", platforms["android"]);
        Assert.Equal("iOS", platforms["ios"]);
        Assert.Equal("Windows", platforms["WINDOWS"]);
    }

    [Fact]
    public void The_default_channels_are_play_direct_and_appstore()
    {
        string[] expected = ["play", "direct", "appstore"];
        Assert.Equal(expected, new AppUpdateOptions().Channels.ToArray());
    }

    [Fact]
    public void A_blank_section_name_is_refused_at_the_start()
    {
        var error = Assert.Throws<ArgumentException>(() => new AppVersionConfigReader(Empty(), new AppUpdateOptions { SectionName = " " }));

        Assert.Equal("SectionName", error.ParamName);
    }

    [Fact]
    public void A_blank_route_is_refused_by_the_client()
    {
        using var http = new HttpClient();

        Assert.Throws<ArgumentException>(() => new AppVersionClient(http, options: new AppUpdateOptions { Route = "" }));
    }

    [Fact]
    public void No_platforms_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new AppVersionManager(
            Empty(),
            NullLogger<AppVersionManager>.Instance,
            new AppUpdateOptions { Platforms = new Dictionary<string, string>() }));
    }

    [Fact]
    public void No_channels_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new AppVersionManager(
            Empty(),
            NullLogger<AppVersionManager>.Instance,
            new AppUpdateOptions { Channels = [] }));
    }

    [Fact]
    public void A_platform_spelled_two_ways_that_differ_only_by_case_is_refused()
    {
        var platforms = new Dictionary<string, string>
        {
            ["android"] = "Android",
            ["ANDROID"] = "Android",
        };

        Assert.Throws<ArgumentException>(() => new AppVersionManager(
            Empty(),
            NullLogger<AppVersionManager>.Instance,
            new AppUpdateOptions { Platforms = platforms }));
    }

    [Fact]
    public void A_blank_code_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new AppVersionManager(
            Empty(),
            NullLogger<AppVersionManager>.Instance,
            new AppUpdateOptions { MisconfiguredCode = " " }));
    }

    [Fact]
    public void A_blank_channel_in_the_list_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new AppVersionManager(
            Empty(),
            NullLogger<AppVersionManager>.Instance,
            new AppUpdateOptions { Channels = ["play", ""] }));
    }

    [Fact]
    public void Without_options_the_defaults_apply()
    {
        var manager = new AppVersionManager(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppVersion:Android:LatestBuild"] = "5" }).Build(),
            NullLogger<AppVersionManager>.Instance);

        Assert.Equal(5, manager.Get("android").LatestBuild);
    }

    [Fact]
    public void The_store_channel_default_is_the_one_the_rules_use()
    {
        var updater = new NoopUpdater();

        Assert.Same(updater, AppVersionRules.StoreUpdaterFor(new AppUpdateOptions().StoreChannel, updater));
    }

    private sealed class NoopUpdater : IAppUpdater
    {
        public bool CanStart(string? storeUrl) => false;

        public Task<AppUpdateStartResult> StartAsync(string? storeUrl) => Task.FromResult(AppUpdateStartResult.NoStoreLink);
    }
}
