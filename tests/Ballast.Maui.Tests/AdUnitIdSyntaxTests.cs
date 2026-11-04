// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Ads;

namespace Ballast.Maui.Tests;

/// <summary>The form of an ad unit id, and the release check that names the field that is wrong.</summary>
public sealed class AdUnitIdSyntaxTests
{
    private const string Banner = "ca-app-pub-9614657079206834/7927871683";
    private const string Native = "ca-app-pub-9614657079206834/1111111111";

    [Theory]
    [InlineData("ca-app-pub-9614657079206834/1111111111", true)]
    [InlineData("ca-app-pub-9614657079206834~7927871683", false)]
    [InlineData("ca-app-pub-961465707920683/1111111111", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Native_id_syntax(string? id, bool valid)
    {
        Assert.Equal(valid, AdUnitIdSyntax.IsValidNativeId(id));
    }

    [Fact]
    public void A_banner_id_has_the_same_form()
    {
        Assert.True(AdUnitIdSyntax.IsValidBannerId(Banner));
        Assert.False(AdUnitIdSyntax.IsValidBannerId("ca-app-pub-/"));
    }

    [Fact]
    public void A_malformed_native_id_is_a_release_error_that_names_the_native_field()
    {
        var error = AdUnitIdSyntax.ReleaseConfigurationError(true, Banner, "ca-app-pub-123/456");

        Assert.NotNull(error);
        Assert.Contains("AndroidNativeAdUnitId", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_banner_id_names_the_banner_field()
    {
        var error = AdUnitIdSyntax.ReleaseConfigurationError(true, "ca-app-pub-/", Native);

        Assert.NotNull(error);
        Assert.Contains("AndroidBannerAdUnitId", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_native_id_is_ignored_while_the_ads_are_off()
    {
        Assert.Null(AdUnitIdSyntax.ReleaseConfigurationError(false, string.Empty, "garbage"));
    }

    [Fact]
    public void Valid_ids_and_an_empty_native_id_are_a_fine_release_configuration()
    {
        Assert.Null(AdUnitIdSyntax.ReleaseConfigurationError(true, Banner, Native));
        Assert.Null(AdUnitIdSyntax.ReleaseConfigurationError(true, Banner, string.Empty));
    }
}
