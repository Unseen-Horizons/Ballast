// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Ads;

/// <summary>What the app's screens ask of the ads: whether to show one, which unit, and the consent answer.</summary>
public interface IAdsService
{
    /// <summary>Whether the ads can start (see <see cref="AdsOptions.CanInitialize"/>).</summary>
    bool IsEnabled { get; }

    /// <summary>The banner unit to request, or null for the network's default.</summary>
    string? BannerAdUnitId { get; }

    /// <summary>Whether native cards are on (see <see cref="AdsOptions.IsNativeEnabled"/>).</summary>
    bool IsNativeEnabled { get; }

    /// <summary>The native unit a card requests; null uses the network's default (the test unit in a debug build).</summary>
    string? NativeAdUnitId { get; }

    /// <summary>
    /// Whether the consent SDK says ads may be requested now. Native requests wait for this, because the consent gate is documented for ad
    /// requests in general and not for native ones in particular.
    /// </summary>
    bool CanRequestAds { get; }

    /// <summary>
    /// Raised when the consent answer may have changed (the consent information was updated, or the privacy form closed). The SDK raises it
    /// on its own thread, so a subscriber moves its work to the UI thread.
    /// </summary>
    event EventHandler? CanRequestAdsChanged;

    /// <summary>Whether the privacy-options entry must be offered.</summary>
    bool IsPrivacyOptionsRequired { get; }

    /// <summary>Opens the privacy-options form, when <see cref="IsPrivacyOptionsRequired"/> is true. Does nothing otherwise.</summary>
    void ShowPrivacyOptions();
}
