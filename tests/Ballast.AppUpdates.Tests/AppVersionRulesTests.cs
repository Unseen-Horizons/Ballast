// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates.Tests;

/// <summary>
/// The decisions behind the update notice: when a build is blocked, when it is only offered an update, when the server is asked
/// again, and which message is shown. Pure, so tested without a platform.
/// </summary>
public class AppVersionRulesTests
{
    private static AppVersionResponse Info(int latest, int minimum = 0) =>
        new() { Platform = "Android", LatestBuild = latest, MinimumBuild = minimum };

    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    // ── Decide ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_build_below_the_minimum_is_required()
    {
        Assert.Equal(AppUpdateDecision.UpdateRequired, AppVersionRules.Decide(7, Info(latest: 12, minimum: 8), null));
    }

    [Fact]
    public void A_build_equal_to_the_minimum_is_not_required()
    {
        Assert.Equal(AppUpdateDecision.UpdateAvailable, AppVersionRules.Decide(8, Info(latest: 12, minimum: 8), null));
    }

    [Fact]
    public void Required_wins_over_a_dismissal()
    {
        Assert.Equal(AppUpdateDecision.UpdateRequired, AppVersionRules.Decide(7, Info(latest: 12, minimum: 8), dismissedLatestBuild: 12));
    }

    [Fact]
    public void A_build_below_latest_is_offered_an_update()
    {
        Assert.Equal(AppUpdateDecision.UpdateAvailable, AppVersionRules.Decide(8, Info(latest: 12), null));
    }

    [Theory]
    [InlineData(12)] // dismissed exactly this build
    [InlineData(15)] // dismissed a later one (the server's latest went back down)
    public void A_dismissal_at_or_above_latest_hides_the_offer(int dismissed)
    {
        Assert.Equal(AppUpdateDecision.UpdateAvailableDismissed, AppVersionRules.Decide(8, Info(latest: 12), dismissed));
    }

    [Fact]
    public void A_dismissal_below_latest_does_not_hide_a_newer_build()
    {
        Assert.Equal(AppUpdateDecision.UpdateAvailable, AppVersionRules.Decide(8, Info(latest: 13), dismissedLatestBuild: 12));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(40)] // a development build above what is published
    public void A_build_at_or_above_latest_is_up_to_date(int current)
    {
        Assert.Equal(AppUpdateDecision.UpToDate, AppVersionRules.Decide(current, Info(latest: 12, minimum: 8), null));
    }

    [Fact]
    public void No_minimum_means_nothing_is_ever_required()
    {
        Assert.Equal(AppUpdateDecision.UpdateAvailable, AppVersionRules.Decide(1, Info(latest: 12, minimum: 0), null));
    }

    // ── The banner: Required is announced, never blocking and never dismissable ─────────────

    [Fact]
    public void Required_shows_the_required_banner()
    {
        var decision = AppVersionRules.Decide(7, Info(latest: 12, minimum: 8), null);

        Assert.Equal(UpdateBannerKind.Required, AppVersionRules.BannerFor(decision));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(7)]   // dismissed a build below the one announced
    [InlineData(12)]  // dismissed exactly the announced build
    [InlineData(99)]  // dismissed a build far above it
    public void Required_is_shown_whatever_was_dismissed(int? dismissed)
    {
        var decision = AppVersionRules.Decide(7, Info(latest: 12, minimum: 8), dismissed);

        Assert.Equal(UpdateBannerKind.Required, AppVersionRules.BannerFor(decision));
    }

    [Fact]
    public void Required_shows_even_when_the_store_has_no_update()
    {
        // Required comes only from the server's minimum; the store saying "nothing newer" cannot hide it.
        var decision = AppVersionRules.Decide(7, Info(latest: 12, minimum: 8), new StoreUpdate(false, 0), dismissedBuild: 12);

        Assert.Equal(UpdateBannerKind.Required, AppVersionRules.BannerFor(decision));
    }

    [Fact]
    public void The_required_banner_cannot_be_dismissed() =>
        Assert.False(AppVersionRules.CanDismiss(UpdateBannerKind.Required));

    [Fact]
    public void An_available_update_is_a_dismissable_offer()
    {
        var decision = AppVersionRules.Decide(8, Info(latest: 12), null);

        Assert.Equal(UpdateBannerKind.Offer, AppVersionRules.BannerFor(decision));
        Assert.True(AppVersionRules.CanDismiss(UpdateBannerKind.Offer));
    }

    [Fact]
    public void A_dismissed_offer_and_an_up_to_date_build_show_no_banner()
    {
        Assert.Equal(UpdateBannerKind.None, AppVersionRules.BannerFor(AppVersionRules.Decide(8, Info(latest: 12), dismissedLatestBuild: 12)));
        Assert.Equal(UpdateBannerKind.None, AppVersionRules.BannerFor(AppVersionRules.Decide(12, Info(latest: 12), null)));
        Assert.Equal(UpdateBannerKind.None, AppVersionRules.BannerFor(AppUpdateDecision.UpToDate));
        Assert.False(AppVersionRules.CanDismiss(UpdateBannerKind.None));
    }

    [Fact]
    public void Every_decision_maps_to_a_banner()
    {
        // A new AppUpdateDecision must say what the banner does with it, not fall through to "nothing".
        foreach (var decision in Enum.GetValues<AppUpdateDecision>())
        {
            var banner = AppVersionRules.BannerFor(decision);
            Assert.Equal(decision is AppUpdateDecision.UpdateRequired, banner == UpdateBannerKind.Required);
            Assert.Equal(decision is AppUpdateDecision.UpdateAvailable, banner == UpdateBannerKind.Offer);
        }
    }

    // ── ShouldCheck ──────────────────────────────────────────────────────────

    [Fact]
    public void No_earlier_check_is_due() =>
        Assert.True(AppVersionRules.ShouldCheck(null, Now, lastWasRequired: false));

    [Fact]
    public void Five_hours_fifty_nine_minutes_is_not_due() =>
        Assert.False(AppVersionRules.ShouldCheck(Now - TimeSpan.FromMinutes(359), Now, lastWasRequired: false));

    [Fact]
    public void Six_hours_is_due() =>
        Assert.True(AppVersionRules.ShouldCheck(Now - TimeSpan.FromHours(6), Now, lastWasRequired: false));

    [Fact]
    public void A_check_in_the_future_is_due_not_fresh() =>
        Assert.True(AppVersionRules.ShouldCheck(Now + TimeSpan.FromDays(2), Now, lastWasRequired: false));

    [Fact]
    public void A_build_below_the_minimum_always_checks_again() =>
        Assert.True(AppVersionRules.ShouldCheck(Now - TimeSpan.FromSeconds(5), Now, lastWasRequired: true));

    [Fact]
    public void The_check_interval_is_six_hours() =>
        Assert.Equal(TimeSpan.FromHours(6), AppVersionRules.CheckInterval);

    // ── PickMessage ──────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> Messages = new() { ["en"] = "Fixes sign-in.", ["hr"] = "Popravlja prijavu." };

    [Theory]
    [InlineData("en", "Fixes sign-in.")]
    [InlineData("hr", "Popravlja prijavu.")]
    [InlineData("HR", "Popravlja prijavu.")]
    public void The_exact_language_is_picked(string language, string expected) =>
        Assert.Equal(expected, AppVersionRules.PickMessage(Messages, language));

    [Fact]
    public void A_missing_language_has_no_message_and_never_falls_back_to_english() =>
        Assert.Null(AppVersionRules.PickMessage(Messages, "de"));

    [Fact]
    public void A_missing_map_or_language_has_no_message()
    {
        Assert.Null(AppVersionRules.PickMessage(null, "en"));
        Assert.Null(AppVersionRules.PickMessage(Messages, ""));
    }

    [Fact]
    public void A_blank_entry_is_no_message() =>
        Assert.Null(AppVersionRules.PickMessage(new Dictionary<string, string> { ["en"] = "   " }, "en"));

    [Fact]
    public void A_message_is_trimmed() =>
        Assert.Equal("Fixes sign-in.", AppVersionRules.PickMessage(new Dictionary<string, string> { ["en"] = "  Fixes sign-in.  " }, "en"));
}
