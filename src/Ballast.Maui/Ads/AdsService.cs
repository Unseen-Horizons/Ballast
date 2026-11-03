// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace Ballast.Maui.Ads;

/// <summary>
/// The app's ad state: whether ads may start, which units to request, and the consent answer, which the consent SDK can change at any
/// time.
/// </summary>
/// <remarks>
/// <para>
/// The consent answer is kept until the consent changes. The SDK says so itself, when it has updated its consent information and when the
/// person closes the privacy form. Every answer is logged, a failure included, because a missing consent form leaves no other trace in a
/// release build.
/// </para>
/// <para>
/// Where there is no consent SDK (no consent given, or the platform does not run ads), the answer is "no": nothing is requested on an answer
/// nobody has. The library needs no platform for this; the platform registration passes the SDK and a dispatcher to the UI thread.
/// </para>
/// </remarks>
public sealed class AdsService : IAdsService
{
    private readonly AdsOptions _options;
    private readonly IAdConsent? _consent;
    private readonly Action<string> _log;
    private readonly Action<Action> _dispatchToUi;
    private readonly object _privacyLock = new();
    private bool? _privacyRequired;
    private int _privacyGeneration;

    /// <summary>Creates the state and, when ads can start, listens to the consent SDK.</summary>
    /// <param name="options">The ads options.</param>
    /// <param name="consent">The consent SDK, or null when there is none.</param>
    /// <param name="log">Where each consent answer and each failure is written. The debugger trace when null.</param>
    /// <param name="dispatchToUi">Runs an action on the UI thread. Runs the action inline when null, as in a test.</param>
    public AdsService(AdsOptions options, IAdConsent? consent = null, Action<string>? log = null, Action<Action>? dispatchToUi = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _log = log ?? (line => Trace.WriteLine(line));
        _dispatchToUi = dispatchToUi ?? (action => action());
        _consent = options.CanInitialize ? consent : null;

        // The SDK raises its events on its own threads, so this only forwards the change; each subscriber moves its work to the UI thread.
        if (_consent is not null)
        {
            _consent.InfoUpdated += (_, info) =>
            {
                _log(ConsentLog.InfoUpdated(info?.Status, info?.CanRequestAds));
                ConsentAnswerChanged();
            };
            _consent.InfoFailedToUpdate += (_, message) => _log(ConsentLog.InfoFailedToUpdate(message));
            _consent.FormError += (_, message) => _log(ConsentLog.FormError(message));
            _consent.FormDismissed += (_, _) => ConsentAnswerChanged();
        }
    }

    /// <inheritdoc />
    public event EventHandler? CanRequestAdsChanged;

    /// <inheritdoc />
    public bool IsEnabled => _options.CanInitialize;

    /// <inheritdoc />
    public string? BannerAdUnitId => _options.EffectiveBannerAdUnitId;

    /// <inheritdoc />
    public bool IsNativeEnabled => _options.IsNativeEnabled;

    /// <inheritdoc />
    public string? NativeAdUnitId => _options.EffectiveNativeAdUnitId;

    /// <summary>
    /// The consent SDK's answer, asked on each call (a card asks once per screen showing, not per frame). A failed query reads as "not
    /// yet", so no request is made on an answer nobody has.
    /// </summary>
    public bool CanRequestAds
    {
        get
        {
            if (_consent is null)
                return false;

            try
            {
                return _consent.CanRequestAds();
            }
            catch (Exception ex)
            {
                _log($"[Ads] Could not read whether ads may be requested: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Whether the privacy-options entry must be offered. The SDK is asked once and the answer kept until its consent information updates or
    /// the privacy form closes. A failed query is not kept, so the next call asks again.
    /// </summary>
    public bool IsPrivacyOptionsRequired
    {
        get
        {
            if (!IsEnabled || _consent is null)
                return false;

            int generation;
            lock (_privacyLock)
            {
                if (_privacyRequired is { } known)
                    return known;
                generation = _privacyGeneration;
            }

            try
            {
                var required = _consent.IsPrivacyOptionsRequired();
                lock (_privacyLock)
                {
                    // Consent changed while the SDK was being asked: this answer may predate it, so it is returned but not kept.
                    if (generation == _privacyGeneration)
                        _privacyRequired = required;
                }

                return required;
            }
            catch (Exception ex)
            {
                _log($"[Ads] Could not read privacy-options status: {ex.Message}");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void ShowPrivacyOptions()
    {
        if (!IsPrivacyOptionsRequired || _consent is not { } consent)
            return;

        _dispatchToUi(() =>
        {
            try
            {
                consent.ShowPrivacyOptionsForm();
            }
            catch (Exception ex)
            {
                _log($"[Ads] Could not open privacy options: {ex.Message}");
            }
        });
    }

    private void InvalidatePrivacyAnswer()
    {
        lock (_privacyLock)
        {
            _privacyGeneration++;
            _privacyRequired = null;
        }
    }

    private void ConsentAnswerChanged()
    {
        InvalidatePrivacyAnswer();
        CanRequestAdsChanged?.Invoke(this, EventArgs.Empty);
    }
}
