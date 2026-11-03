// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Ads;

/// <summary>
/// When a native ad card may start a load. Pure, so the rule is pinned without a device.
/// </summary>
public static class NativeAdLoadRule
{
    /// <summary>The default span in which a card loads at most once, however often its screen is shown.</summary>
    public static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// True when a load may start: none has started yet, or the last one started at least <paramref name="minimumInterval"/> ago. A
    /// negative span means the clock went back, and the load is allowed rather than blocked until it catches up.
    /// </summary>
    /// <param name="sinceLastStart">The time since the last load started, or null when none has.</param>
    /// <param name="minimumInterval">The span between two loads.</param>
    /// <returns>True when a load may start.</returns>
    public static bool MayStartLoad(TimeSpan? sinceLastStart, TimeSpan minimumInterval)
    {
        if (minimumInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumInterval), minimumInterval, "The interval must not be negative.");
        return sinceLastStart is not { } elapsed || elapsed < TimeSpan.Zero || elapsed >= minimumInterval;
    }
}
