// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Ads;

namespace Ballast.Maui.Adapters;

/// <summary>
/// The Plugin.AdMob consent service, as <see cref="IAdConsent"/>. The app gets the service from its own container, where <c>UseAdMob</c> registered
/// it, and passes it here. Create one only when the ads can start.
/// </summary>
public sealed class AdMobConsent : IAdConsent
{
    private readonly Plugin.AdMob.Services.IAdConsentService _service;

    /// <summary>Listens to the consent service's events and forwards them.</summary>
    /// <param name="service">The consent service the ad network registered.</param>
    public AdMobConsent(Plugin.AdMob.Services.IAdConsentService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;

        _service.OnConsentInfoUpdated += (_, info) => InfoUpdated?.Invoke(this, new ConsentInfo(info?.Status.ToString(), info?.CanRequestAds));
        _service.OnConsentInfoFailedToUpdate += (_, error) => InfoFailedToUpdate?.Invoke(this, error?.Message);
        _service.OnConsentFormError += (_, error) => FormError?.Invoke(this, error?.Message);
        _service.OnConsentFormDismissed += (_, _) => FormDismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler<ConsentInfo>? InfoUpdated;

    /// <inheritdoc />
    public event EventHandler<string?>? InfoFailedToUpdate;

    /// <inheritdoc />
    public event EventHandler<string?>? FormError;

    /// <inheritdoc />
    public event EventHandler? FormDismissed;

    /// <inheritdoc />
    public bool CanRequestAds() => _service.CanRequestAds();

    /// <inheritdoc />
    public bool IsPrivacyOptionsRequired() => _service.IsPrivacyOptionsRequired();

    /// <inheritdoc />
    public void ShowPrivacyOptionsForm() => _service.ShowPrivacyOptionsForm();
}
