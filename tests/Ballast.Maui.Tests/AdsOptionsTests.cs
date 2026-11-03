// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

using Ballast.Maui.Ads;

namespace Ballast.Maui.Tests;

/// <summary>How the ads options are read from a document, and which switches and unit ids follow from them.</summary>
public sealed class AdsOptionsTests
{
    private const string Banner = "ca-app-pub-9614657079206834/7927871683";
    private const string Native = "ca-app-pub-9614657079206834/1111111111";

    private static AdsOptions ReadJson(string json) => AdsOptions.Read(JsonDocument.Parse(json));

    private static string Section(string nativeValue) =>
        "{\"Ads\":{\"Enabled\":true,\"AndroidBannerAdUnitId\":\"" + Banner + "\",\"AndroidNativeAdUnitId\":" + nativeValue + "}}";

    [Fact]
    public void The_native_id_is_read_and_trimmed()
    {
        var options = ReadJson(Section($"\"  {Native}  \""));

        Assert.Equal(Native, options.AndroidNativeAdUnitId);
        Assert.True(options.HasValidProductionNativeId);
    }

    [Fact]
    public void An_empty_native_id_is_off_and_the_banner_is_unaffected()
    {
        var options = ReadJson(Section("\"\""));

        Assert.False(options.HasValidProductionNativeId);
        Assert.Equal(Banner, options.AndroidBannerAdUnitId);
        Assert.Null(AdUnitIdSyntax.ReleaseConfigurationError(options.Enabled, options.AndroidBannerAdUnitId, options.AndroidNativeAdUnitId));
    }

    [Fact]
    public void A_missing_or_non_string_native_value_reads_as_empty()
    {
        Assert.Equal(string.Empty, ReadJson("{\"Ads\":{\"Enabled\":true,\"AndroidBannerAdUnitId\":\"" + Banner + "\"}}").AndroidNativeAdUnitId);
        Assert.Equal(string.Empty, ReadJson(Section("42")).AndroidNativeAdUnitId);
    }

    [Fact]
    public void A_missing_document_or_section_leaves_ads_off()
    {
        Assert.False(AdsOptions.Read(null).Enabled);
        Assert.False(ReadJson("{}").Enabled);
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("false")]
    public void Only_a_json_true_switches_the_ads_on(string value)
    {
        Assert.False(ReadJson("{\"Ads\":{\"Enabled\":" + value + "}}").Enabled);
    }

    [Fact]
    public void Ads_do_not_start_on_a_platform_that_does_not_run_them()
    {
        var options = ReadJson(Section($"\"{Native}\"")) with { PlatformSupportsAds = false, IsDebugBuild = false };

        Assert.False(options.CanInitialize);
    }

    [Fact]
    public void A_release_build_needs_a_well_formed_banner_id_to_start()
    {
        var release = new AdsOptions { Enabled = true, PlatformSupportsAds = true, AndroidBannerAdUnitId = "ca-app-pub-/" };
        var debug = release with { IsDebugBuild = true };

        Assert.False(release.CanInitialize);
        Assert.True(debug.CanInitialize);
    }

    [Fact]
    public void A_debug_build_uses_the_network_test_units_and_never_the_production_ids()
    {
        var options = ReadJson(Section($"\"{Native}\"")) with { PlatformSupportsAds = true, IsDebugBuild = true };

        Assert.True(options.CanInitialize);
        Assert.Null(options.EffectiveBannerAdUnitId);
        Assert.Null(options.EffectiveNativeAdUnitId);
    }

    [Fact]
    public void A_release_build_uses_the_configured_units_and_turns_the_native_cards_on()
    {
        var options = ReadJson(Section($"\"{Native}\"")) with { PlatformSupportsAds = true, IsDebugBuild = false };

        Assert.Equal(Banner, options.EffectiveBannerAdUnitId);
        Assert.Equal(Native, options.EffectiveNativeAdUnitId);
        Assert.True(options.IsNativeEnabled);
    }

    [Fact]
    public void A_release_build_with_no_native_id_keeps_the_banner_on_and_the_native_cards_off()
    {
        var options = ReadJson(Section("\"\"")) with { PlatformSupportsAds = true, IsDebugBuild = false };

        Assert.True(options.CanInitialize);
        Assert.False(options.IsNativeEnabled);
    }

    [Fact]
    public void The_defaults_are_the_library_defaults()
    {
        var options = new AdsOptions();

        Assert.False(options.Enabled);
        Assert.Equal("Ad", options.BadgeText);
        Assert.Equal("BorderSubtleLight", options.DividerLightResourceKey);
        Assert.Equal("DarkBorder", options.DividerDarkResourceKey);
        Assert.Equal("Ballast.Maui.Ads", options.LogTag);
        Assert.Empty(options.AdFreePageTypeNames);
    }
}
