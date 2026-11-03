// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Android.Gms.Extensions;

using Ballast.Maui.Diagnostics;

using Xamarin.Google.Android.Play.Core.Integrity;

namespace Ballast.Maui.AppAttestation;

/// <summary>
/// Android's attestation: a Google Play Integrity token bound to the server's nonce. The server decodes it with Google and decides. This
/// class only asks the device for it.
/// </summary>
/// <remarks>
/// Never throws: any failure (no Play services, the request throttled, an emulator without Play) is logged and returns null. The server is
/// still asked, and its answer is what the rider sees. It is never an offline substitute.
/// </remarks>
public sealed class PlayIntegrityProvider : IAppAttestation
{
    private readonly long? _projectNumber;
    private readonly IClientLog _log;

    /// <summary>Creates the provider.</summary>
    /// <param name="options">The Google Cloud project number, when the app has one.</param>
    /// <param name="log">Where a failed request is recorded.</param>
    public PlayIntegrityProvider(PlayIntegrityOptions options, IClientLog log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _projectNumber = PlayIntegrityOptions.ParseProjectNumber(options.CloudProjectNumber);
        _log = log;
    }

    /// <inheritdoc />
    public string Platform => "android";

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync(string nonce, CancellationToken ct = default)
    {
        try
        {
            // The application context, not an activity: attestation can run before one is in front.
            var manager = IntegrityManagerFactory.Create(global::Android.App.Application.Context)
                ?? throw new InvalidOperationException("Play Integrity is not available on this device.");
            var builder = IntegrityTokenRequest.InvokeBuilder()?.SetNonce(nonce)
                ?? throw new InvalidOperationException("Play Integrity returned no request builder.");
            if (_projectNumber is { } project)
            {
                builder = builder.SetCloudProjectNumber(project)
                    ?? throw new InvalidOperationException("Play Integrity returned no request builder.");
            }

            var request = builder.Build() ?? throw new InvalidOperationException("Play Integrity built no request.");
            var task = manager.RequestIntegrityToken(request)
                ?? throw new InvalidOperationException("Play Integrity returned no task.");
            var response = await task.AsAsync<IntegrityTokenResponse>().WaitAsync(ct).ConfigureAwait(false);
            var token = response.Token();
            if (string.IsNullOrWhiteSpace(token))
            {
                _log.Warning("PlayIntegrity", "Play returned an empty token.");
                return null;
            }

            return token;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning("PlayIntegrity", $"Could not get an integrity token ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }
    }
}
