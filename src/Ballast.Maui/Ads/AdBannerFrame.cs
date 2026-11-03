// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Ads;

/// <summary>
/// The space the one banner takes: a gap, a divider, the ad, and a gap before the tab bar. Pure, so a test pins it. Nothing in the frame
/// is tappable; only the ad inside it is.
/// </summary>
public static class AdBannerFrame
{
    /// <summary>The space between the controls above and the divider, and between the ad and the tab bar.</summary>
    public const double GapDp = 8;

    /// <summary>The divider line above the ad.</summary>
    public const double DividerDp = 1;

    /// <summary>
    /// The slot's total height for an ad of <paramref name="adaptiveDp"/>, or 0 while there is no ad height (the slot is collapsed: no ad
    /// has been reserved, or the request failed).
    /// </summary>
    /// <param name="adaptiveDp">The ad's height in device-independent pixels, or null.</param>
    /// <returns>The frame's height in device-independent pixels.</returns>
    public static double Height(double? adaptiveDp) =>
        adaptiveDp is { } adaptive and > 0 ? GapDp + DividerDp + adaptive + GapDp : 0;
}
