// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Ads;

namespace Ballast.Maui.Tests;

/// <summary>The height the one banner takes: a gap, a divider, the ad and a gap, or nothing while there is no ad.</summary>
public sealed class AdBannerFrameTests
{
    [Fact]
    public void The_frame_is_a_gap_a_divider_the_ad_and_a_gap()
    {
        Assert.Equal(8d + 1d + 50d + 8d, AdBannerFrame.Height(50));
    }

    [Fact]
    public void A_collapsed_frame_takes_no_height()
    {
        Assert.Equal(0d, AdBannerFrame.Height(null));
        Assert.Equal(0d, AdBannerFrame.Height(0));
    }

    [Fact]
    public void A_negative_ad_height_takes_no_height()
    {
        Assert.Equal(0d, AdBannerFrame.Height(-1));
    }
}
