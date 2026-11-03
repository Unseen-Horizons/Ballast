// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Ads;

namespace Ballast.Maui.Tests;

/// <summary>How often a native card may start a load: at once the first time, and then after the minimum interval.</summary>
public sealed class NativeAdLoadRuleTests
{
    private static readonly TimeSpan Minimum = NativeAdLoadRule.DefaultMinimumInterval;

    [Fact]
    public void A_first_load_may_start()
    {
        Assert.True(NativeAdLoadRule.MayStartLoad(null, Minimum));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(600, true)]
    public void A_load_waits_the_minimum_interval_after_the_last_one_started(int seconds, bool allowed)
    {
        Assert.Equal(allowed, NativeAdLoadRule.MayStartLoad(TimeSpan.FromSeconds(seconds), Minimum));
    }

    [Fact]
    public void A_negative_elapsed_time_counts_as_allowed()
    {
        Assert.True(NativeAdLoadRule.MayStartLoad(TimeSpan.FromSeconds(-5), Minimum));
    }

    [Fact]
    public void The_default_interval_is_a_minute()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), NativeAdLoadRule.DefaultMinimumInterval);
    }

    [Fact]
    public void A_negative_minimum_interval_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeAdLoadRule.MayStartLoad(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1)));
    }
}
