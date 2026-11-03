// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Ads;

/// <summary>The consent information the SDK reported when it updated.</summary>
/// <param name="Status">The SDK's status, as text, or null when it gave none.</param>
/// <param name="CanRequestAds">Whether ads may be requested, or null when the SDK gave no answer.</param>
public sealed record ConsentInfo(string? Status, bool? CanRequestAds);

/// <summary>
/// The ad network's consent SDK, as the library needs it. On Android the production implementation wraps the Plugin.AdMob consent
/// service; a test supplies a fake.
/// </summary>
public interface IAdConsent
{
    /// <summary>Whether the SDK says ads may be requested now.</summary>
    /// <returns>True when ads may be requested.</returns>
    bool CanRequestAds();

    /// <summary>Whether the SDK says the privacy-options entry must be offered.</summary>
    /// <returns>True when it must be offered.</returns>
    bool IsPrivacyOptionsRequired();

    /// <summary>Opens the SDK's privacy-options form.</summary>
    void ShowPrivacyOptionsForm();

    /// <summary>Raised when the SDK updated its consent information. Raised on the SDK's own thread.</summary>
    event EventHandler<ConsentInfo>? InfoUpdated;

    /// <summary>Raised when the SDK could not update its consent information. The argument is the SDK's message, or null.</summary>
    event EventHandler<string?>? InfoFailedToUpdate;

    /// <summary>Raised when the consent form could not be shown. The argument is the SDK's message, or null.</summary>
    event EventHandler<string?>? FormError;

    /// <summary>Raised when the privacy form was closed.</summary>
    event EventHandler? FormDismissed;
}
