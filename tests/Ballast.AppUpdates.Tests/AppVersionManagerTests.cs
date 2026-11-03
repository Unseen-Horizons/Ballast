// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Ballast.Errors;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.AppUpdates.Tests;

/// <summary>
/// The version answer: what the server says about builds, and that a configuration that does not hold together is an error rather than
/// something quietly repaired (a wrong minimum locks every rider out).
/// </summary>
public class AppVersionManagerTests
{
    private static AppVersionManager Manager(Dictionary<string, string?> settings, CapturingLogger<AppVersionManager>? logger = null, AppUpdateOptions? options = null) =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            logger ?? new CapturingLogger<AppVersionManager>(),
            options);

    private static Dictionary<string, string?> Android(int latest = 12, int? minimum = 8) => new()
    {
        ["AppVersion:Android:LatestBuild"] = latest.ToString(CultureInfo.InvariantCulture),
        ["AppVersion:Android:MinimumBuild"] = minimum?.ToString(CultureInfo.InvariantCulture),
        ["AppVersion:Android:LatestVersion"] = "0.2.0",
        ["AppVersion:Android:StoreUrl"] = "https://play.google.com/store/apps/details?id=com.example.app",
        ["AppVersion:Android:Messages:en"] = "Fixes sign-in.",
        ["AppVersion:Android:Messages:hr"] = "Popravlja prijavu.",
    };

    [Fact]
    public void A_configured_platform_returns_every_field()
    {
        var response = Manager(Android()).Get("android");

        Assert.Equal("Android", response.Platform);
        Assert.Equal(12, response.LatestBuild);
        Assert.Equal(8, response.MinimumBuild);
        Assert.Equal("0.2.0", response.LatestVersion);
        Assert.Equal("https://play.google.com/store/apps/details?id=com.example.app", response.StoreUrl);
    }

    [Theory]
    [InlineData("android")]
    [InlineData("ANDROID")]
    [InlineData("Android")]
    public void The_platform_is_case_insensitive(string platform) =>
        Assert.Equal("Android", Manager(Android()).Get(platform).Platform);

    [Theory]
    [InlineData("symbian")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unknown_or_missing_platform_is_a_400(string? platform)
    {
        var ex = Assert.Throws<AppException>(() => Manager(Android()).Get(platform));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("APP_PLATFORM_UNKNOWN", ex.Code);
    }

    [Fact]
    public void The_unknown_platform_answer_names_the_platforms_it_knows()
    {
        var ex = Assert.Throws<AppException>(() => Manager(Android()).Get("symbian"));

        Assert.Equal("platform must be one of: android, ios, windows.", ex.Detail);
    }

    [Fact]
    public void A_platform_with_no_LatestBuild_is_not_configured()
    {
        // Only the store link is set, as appsettings.json ships it.
        var manager = Manager(new() { ["AppVersion:Android:StoreUrl"] = "https://play.google.com/store/apps/details?id=x" });

        var ex = Assert.Throws<AppException>(() => manager.Get("android"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("APP_VERSION_NOT_CONFIGURED", ex.Code);
    }

    [Fact]
    public void A_platform_with_no_section_at_all_is_not_configured()
    {
        var ex = Assert.Throws<AppException>(() => Manager(Android()).Get("windows"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("APP_VERSION_NOT_CONFIGURED", ex.Code);
    }

    [Fact]
    public void A_minimum_above_the_latest_is_misconfigured_and_logged_not_clamped()
    {
        var logger = new CapturingLogger<AppVersionManager>();

        var ex = Assert.Throws<AppException>(() => Manager(Android(latest: 10, minimum: 11), logger).Get("android"));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("APP_VERSION_MISCONFIGURED", ex.Code);
        Assert.Null(ex.Detail);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("MinimumBuild 11"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void A_latest_build_that_is_not_a_positive_whole_number_is_misconfigured(string latest)
    {
        var logger = new CapturingLogger<AppVersionManager>();
        var settings = Android();
        settings["AppVersion:Android:LatestBuild"] = latest;

        var ex = Assert.Throws<AppException>(() => Manager(settings, logger).Get("android"));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("APP_VERSION_MISCONFIGURED", ex.Code);
        Assert.Null(ex.Detail);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public void A_minimum_that_is_not_a_whole_number_is_misconfigured(string minimum)
    {
        var settings = Android();
        settings["AppVersion:Android:MinimumBuild"] = minimum;

        var ex = Assert.Throws<AppException>(() => Manager(settings).Get("android"));

        Assert.Equal("APP_VERSION_MISCONFIGURED", ex.Code);
        Assert.Null(ex.Detail);
    }

    [Fact]
    public void A_minimum_equal_to_the_latest_is_allowed()
    {
        var response = Manager(Android(latest: 9, minimum: 9)).Get("android");

        Assert.Equal(9, response.MinimumBuild);
    }

    [Fact]
    public void An_absent_minimum_is_zero()
    {
        var response = Manager(Android(minimum: null)).Get("android");

        Assert.Equal(0, response.MinimumBuild);
    }

    [Theory]
    [InlineData("http://play.google.com/store/apps/details?id=x")]
    [InlineData("/store/apps")]
    [InlineData("play.google.com/store")]
    [InlineData("javascript:alert(1)")]
    public void A_store_url_that_is_not_absolute_https_is_dropped_with_a_warning(string url)
    {
        var logger = new CapturingLogger<AppVersionManager>();
        var settings = Android();
        settings["AppVersion:Android:StoreUrl"] = url;

        var response = Manager(settings, logger).Get("android");

        Assert.Null(response.StoreUrl);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(12, response.LatestBuild);
    }

    [Fact]
    public void An_absent_store_url_and_version_text_are_null()
    {
        var response = Manager(new() { ["AppVersion:Android:LatestBuild"] = "5" }).Get("android");

        Assert.Null(response.StoreUrl);
        Assert.Null(response.LatestVersion);
        Assert.Empty(response.Messages);
    }

    [Fact]
    public void Messages_are_carried_through_by_language()
    {
        var response = Manager(Android()).Get("android");

        Assert.Equal(2, response.Messages.Count);
        Assert.Equal("Fixes sign-in.", response.Messages["en"]);
        Assert.Equal("Popravlja prijavu.", response.Messages["hr"]);
    }

    // -- Install channels ---------------------------------------------------------------------------

    private const string BlobUrl = "https://storage.example.test/releases/android/app-12.apk";

    private static Dictionary<string, string?> AndroidWithDirect(int directLatest = 12, int? directMinimum = null)
    {
        var settings = Android(latest: 20, minimum: 8);
        settings["AppVersion:Android:direct:LatestBuild"] = directLatest.ToString(CultureInfo.InvariantCulture);
        settings["AppVersion:Android:direct:MinimumBuild"] = directMinimum?.ToString(CultureInfo.InvariantCulture);
        settings["AppVersion:Android:direct:LatestVersion"] = "0.1.9";
        settings["AppVersion:Android:direct:StoreUrl"] = BlobUrl;
        return settings;
    }

    [Fact]
    public void The_channel_section_wins_over_the_platform_section()
    {
        var response = Manager(AndroidWithDirect(directLatest: 12)).Get("android", "direct");

        Assert.Equal(12, response.LatestBuild);
        Assert.Equal(0, response.MinimumBuild);
        Assert.Equal("0.1.9", response.LatestVersion);
        Assert.Equal(BlobUrl, response.StoreUrl);
        Assert.Equal("direct", response.Channel);
        // Never merged key by key: the platform's messages and minimum do not leak into the channel.
        Assert.Empty(response.Messages);
    }

    [Fact]
    public void A_channel_with_no_section_falls_back_to_the_platform_section()
    {
        var response = Manager(Android()).Get("android", "direct");

        Assert.Equal(12, response.LatestBuild);
        Assert.Equal(8, response.MinimumBuild);
        Assert.Null(response.Channel);
    }

    [Fact]
    public void A_channel_section_with_only_a_store_url_has_no_numbers_so_the_platform_answers()
    {
        var settings = Android();
        settings["AppVersion:Android:play:StoreUrl"] = "https://play.google.com/store/apps/details?id=x";

        var response = Manager(settings).Get("android", "play");

        Assert.Null(response.Channel);
        Assert.Equal(12, response.LatestBuild);
    }

    [Fact]
    public void No_channel_asks_the_platform_section_even_when_a_channel_section_exists()
    {
        var response = Manager(AndroidWithDirect()).Get("android");

        Assert.Equal(20, response.LatestBuild);
        Assert.Null(response.Channel);
    }

    [Theory]
    [InlineData("DIRECT")]
    [InlineData(" Direct ")]
    public void The_channel_is_case_and_whitespace_insensitive(string channel) =>
        Assert.Equal("direct", Manager(AndroidWithDirect()).Get("android", channel).Channel);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_channel_is_no_channel(string? channel) =>
        Assert.Null(Manager(AndroidWithDirect()).Get("android", channel).Channel);

    [Theory]
    [InlineData("sideload")]
    [InlineData("play store")]
    [InlineData("android")]
    public void An_unknown_channel_is_a_400(string channel)
    {
        var ex = Assert.Throws<AppException>(() => Manager(AndroidWithDirect()).Get("android", channel));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("APP_CHANNEL_UNKNOWN", ex.Code);
    }

    [Fact]
    public void A_direct_store_url_that_is_a_blob_url_is_accepted()
    {
        var response = Manager(AndroidWithDirect()).Get("android", "direct");

        Assert.Equal(BlobUrl, response.StoreUrl);
    }

    [Fact]
    public void A_direct_store_url_that_is_not_https_is_dropped_with_a_warning_and_not_replaced_by_the_platform_link()
    {
        var logger = new CapturingLogger<AppVersionManager>();
        var settings = AndroidWithDirect();
        settings["AppVersion:Android:direct:StoreUrl"] = "http://example.com/app.apk";

        var response = Manager(settings, logger).Get("android", "direct");

        Assert.Null(response.StoreUrl);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Android:direct"));
        Assert.Equal(12, response.LatestBuild);
    }

    [Fact]
    public void A_channel_that_has_numbers_but_no_store_url_has_none_rather_than_the_platforms()
    {
        var settings = AndroidWithDirect();
        settings["AppVersion:Android:direct:StoreUrl"] = null;

        var response = Manager(settings).Get("android", "direct");

        Assert.Null(response.StoreUrl);
    }

    [Fact]
    public void A_channel_that_is_only_a_minimum_answers_with_no_latest_build()
    {
        // The store channel: the store says whether a newer build exists, the server only retires old ones.
        var settings = new Dictionary<string, string?>
        {
            ["AppVersion:Android:play:MinimumBuild"] = "9",
            ["AppVersion:Android:play:StoreUrl"] = "https://play.google.com/store/apps/details?id=x",
        };

        var response = Manager(settings).Get("android", "play");

        Assert.Equal(0, response.LatestBuild);
        Assert.Equal(9, response.MinimumBuild);
        Assert.Equal("play", response.Channel);
        Assert.NotNull(response.StoreUrl);
    }

    [Fact]
    public void Validation_applies_to_the_resolved_channel_section_and_names_it()
    {
        var logger = new CapturingLogger<AppVersionManager>();

        var ex = Assert.Throws<AppException>(() => Manager(AndroidWithDirect(directLatest: 10, directMinimum: 11), logger).Get("android", "direct"));

        Assert.Equal("APP_VERSION_MISCONFIGURED", ex.Code);
        Assert.Null(ex.Detail);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Android:direct") && e.Message.Contains("MinimumBuild 11"));
        // The platform section is fine and still answers a request that sends no channel.
        Assert.Equal(20, Manager(AndroidWithDirect(directLatest: 10, directMinimum: 11)).Get("android").LatestBuild);
    }

    [Fact]
    public void A_channel_with_no_numbers_anywhere_is_not_configured()
    {
        var ex = Assert.Throws<AppException>(() => Manager(new() { ["AppVersion:Android:StoreUrl"] = "https://play.google.com/x" }).Get("android", "direct"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("APP_VERSION_NOT_CONFIGURED", ex.Code);
    }

    // -- Settings that differ from the defaults ----------------------------------------------------

    [Fact]
    public void A_platform_the_options_add_is_answered_under_its_own_section_name()
    {
        var options = new AppUpdateOptions { Platforms = new Dictionary<string, string> { ["tvos"] = "tvOS" } };
        var settings = new Dictionary<string, string?> { ["AppVersion:tvOS:LatestBuild"] = "3" };

        var response = Manager(settings, options: options).Get("TVOS");

        Assert.Equal("tvOS", response.Platform);
        Assert.Equal(3, response.LatestBuild);
        Assert.ThrowsAny<AppException>(() => Manager(Android(), options: options).Get("android"));
    }

    [Fact]
    public void The_options_name_the_codes_the_answers_carry()
    {
        var options = new AppUpdateOptions
        {
            PlatformUnknownCode = "XX_PLATFORM",
            ChannelUnknownCode = "XX_CHANNEL",
            NotConfiguredCode = "XX_NOT_SET",
            MisconfiguredCode = "XX_BAD",
        };

        Assert.Equal("XX_PLATFORM", Assert.Throws<AppException>(() => Manager(Android(), options: options).Get("symbian")).Code);
        Assert.Equal("XX_CHANNEL", Assert.Throws<AppException>(() => Manager(Android(), options: options).Get("android", "sideload")).Code);
        Assert.Equal("XX_NOT_SET", Assert.Throws<AppException>(() => Manager(new(), options: options).Get("android")).Code);
        Assert.Equal("XX_BAD", Assert.Throws<AppException>(() => Manager(Android(latest: 10, minimum: 11), options: options).Get("android")).Code);
    }

    [Fact]
    public void The_options_name_the_channels_the_answers_accept()
    {
        var options = new AppUpdateOptions { Channels = ["beta"] };
        var settings = new Dictionary<string, string?> { ["AppVersion:Android:beta:LatestBuild"] = "30" };

        Assert.Equal("beta", Manager(settings, options: options).Get("android", "BETA").Channel);
        Assert.Equal("APP_CHANNEL_UNKNOWN", Assert.Throws<AppException>(() => Manager(settings, options: options).Get("android", "direct")).Code);
    }

    [Fact]
    public void The_section_name_is_an_option()
    {
        var options = new AppUpdateOptions { SectionName = "Versions" };
        var settings = new Dictionary<string, string?> { ["Versions:Android:LatestBuild"] = "7" };

        Assert.Equal(7, Manager(settings, options: options).Get("android").LatestBuild);
        Assert.Equal("APP_VERSION_NOT_CONFIGURED", Assert.Throws<AppException>(() => Manager(Android(), options: options).Get("android")).Code);
    }

    [Fact]
    public void The_misconfiguration_log_names_the_configured_section()
    {
        var logger = new CapturingLogger<AppVersionManager>();
        var options = new AppUpdateOptions { SectionName = "Versions" };

        Assert.Throws<AppException>(() => Manager(new() { ["Versions:Android:LatestBuild"] = "x" }, logger, options).Get("android"));

        Assert.Contains(logger.Entries, e => e.Message.StartsWith("Versions:Android", StringComparison.Ordinal));
    }
}
