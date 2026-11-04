// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Ads;

namespace Ballast.Maui.Tests;

/// <summary>The ads state: the consent answers and the privacy answer it keeps, what is logged, and what is requested in each build.</summary>
public sealed class AdsServiceTests
{
    private const string Banner = "ca-app-pub-1234567890123456/1234567890";
    private const string Native = "ca-app-pub-1234567890123456/9876543210";

    private static AdsOptions DebugOn() => new() { Enabled = true, PlatformSupportsAds = true, IsDebugBuild = true };

    private static AdsOptions ReleaseOn() => new()
    {
        Enabled = true,
        PlatformSupportsAds = true,
        IsDebugBuild = false,
        AndroidBannerAdUnitId = Banner,
        AndroidNativeAdUnitId = Native,
    };

    [Fact]
    public void Disabled_options_ignore_the_consent_sdk_entirely()
    {
        var consent = new FakeAdConsent { CanRequest = true, PrivacyRequired = true };
        var lines = new List<string>();
        var service = new AdsService(new AdsOptions(), consent, lines.Add);

        Assert.False(service.IsEnabled);
        Assert.False(service.CanRequestAds);
        Assert.False(service.IsPrivacyOptionsRequired);
        consent.RaiseInfoUpdated(new ConsentInfo("Required", true));

        Assert.Empty(lines);
        Assert.Equal(0, consent.PrivacyQueries);
    }

    [Fact]
    public void Without_a_consent_sdk_no_ad_may_be_requested()
    {
        var service = new AdsService(DebugOn(), null, _ => { });

        Assert.True(service.IsEnabled);
        Assert.False(service.CanRequestAds);
        Assert.False(service.IsPrivacyOptionsRequired);
    }

    [Fact]
    public void CanRequestAds_reads_the_sdk_and_a_failed_read_is_logged_as_no()
    {
        var consent = new FakeAdConsent { CanRequest = true };
        var lines = new List<string>();
        var service = new AdsService(DebugOn(), consent, lines.Add);
        Assert.True(service.CanRequestAds);

        consent.Fault = new InvalidOperationException("boom");

        Assert.False(service.CanRequestAds);
        Assert.Contains(lines, line => line.Contains("Could not read whether ads may be requested: boom", StringComparison.Ordinal));
    }

    [Fact]
    public void The_privacy_answer_is_asked_once_and_kept_until_the_consent_changes()
    {
        var consent = new FakeAdConsent { PrivacyRequired = true };
        var service = new AdsService(DebugOn(), consent, _ => { });

        Assert.True(service.IsPrivacyOptionsRequired);
        Assert.True(service.IsPrivacyOptionsRequired);
        Assert.Equal(1, consent.PrivacyQueries);

        consent.PrivacyRequired = false;
        consent.RaiseInfoUpdated(new ConsentInfo("Required", true));

        Assert.False(service.IsPrivacyOptionsRequired);
        Assert.Equal(2, consent.PrivacyQueries);
    }

    [Fact]
    public void A_failed_privacy_query_is_not_kept_so_the_next_call_asks_again()
    {
        var consent = new FakeAdConsent { Fault = new InvalidOperationException("boom") };
        var lines = new List<string>();
        var service = new AdsService(DebugOn(), consent, lines.Add);

        Assert.False(service.IsPrivacyOptionsRequired);
        Assert.Contains(lines, line => line.Contains("Could not read privacy-options status: boom", StringComparison.Ordinal));

        consent.Fault = null;
        consent.PrivacyRequired = true;

        Assert.True(service.IsPrivacyOptionsRequired);
        Assert.Equal(1, consent.PrivacyQueries);
    }

    [Fact]
    public void Each_consent_answer_is_logged_as_the_consent_log_writes_it()
    {
        var consent = new FakeAdConsent();
        var lines = new List<string>();
        _ = new AdsService(DebugOn(), consent, lines.Add);

        consent.RaiseInfoUpdated(new ConsentInfo("Required", false));
        consent.RaiseInfoFailedToUpdate("Publisher misconfiguration");
        consent.RaiseFormError(null);

        Assert.Equal(
        [
            ConsentLog.InfoUpdated("Required", false),
            ConsentLog.InfoFailedToUpdate("Publisher misconfiguration"),
            ConsentLog.FormError(null),
        ], lines);
    }

    [Fact]
    public void A_consent_update_and_a_closed_form_both_announce_a_change()
    {
        var consent = new FakeAdConsent();
        var service = new AdsService(DebugOn(), consent, _ => { });
        var changes = 0;
        service.CanRequestAdsChanged += (_, _) => changes++;

        consent.RaiseInfoUpdated(new ConsentInfo("Obtained", true));
        consent.RaiseFormDismissed();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Show_privacy_options_hands_the_form_to_the_ui_only_when_it_is_required()
    {
        var consent = new FakeAdConsent { PrivacyRequired = true };
        var queued = new List<Action>();
        var service = new AdsService(DebugOn(), consent, _ => { }, queued.Add);

        service.ShowPrivacyOptions();

        Assert.Single(queued);
        Assert.Equal(0, consent.FormsShown);
        queued[0]();
        Assert.Equal(1, consent.FormsShown);

        consent.PrivacyRequired = false;
        var notRequired = new AdsService(DebugOn(), consent, _ => { }, queued.Add);
        queued.Clear();
        notRequired.ShowPrivacyOptions();

        Assert.Empty(queued);
    }

    [Fact]
    public void A_release_build_hands_out_its_configured_units_and_a_debug_build_hands_out_none()
    {
        var release = new AdsService(ReleaseOn(), null, _ => { });
        Assert.Equal(Banner, release.BannerAdUnitId);
        Assert.Equal(Native, release.NativeAdUnitId);
        Assert.True(release.IsNativeEnabled);

        var debug = new AdsService(DebugOn(), null, _ => { });
        Assert.Null(debug.BannerAdUnitId);
        Assert.Null(debug.NativeAdUnitId);
    }
}
