// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Google.Apis.Auth.OAuth2;
using Google.Apis.PlayIntegrity.v1;
using Google.Apis.PlayIntegrity.v1.Data;
using Google.Apis.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation;

/// <summary>
/// Decodes an integrity token with Google's <c>decodeIntegrityToken</c>, signed in as the service account in
/// <see cref="PlayIntegrityOptions.ServiceAccountJson"/>. The only class in the library that calls Google for attestation.
/// Everything it returns goes through <see cref="IntegrityVerdict"/>, so the rest is tested without it.
/// <para>
/// Any failure to get an answer (no credentials configured, a bad credential, Google down or refusing, a malformed response) is an
/// <see cref="AttestationUnavailableException"/>: logged here, answered as 503, and never read as attested.
/// </para>
/// </summary>
public sealed class GooglePlayIntegrityDecoder : IPlayIntegrityDecoder, IDisposable
{
    private readonly string _serviceAccountJson;
    private readonly string _applicationName;
    private readonly string _packageName;
    private readonly string _serviceAccountSetting;
    private readonly ILogger<GooglePlayIntegrityDecoder> _logger;
    private readonly Lock _gate = new();
    private PlayIntegrityService? _service;

    /// <summary>Creates the decoder. It calls Google only when a token is decoded, and builds its client on the first call.</summary>
    /// <param name="playIntegrity">The Google settings: the service account and the application name.</param>
    /// <param name="setup">The setup, whose package name the decoder asks Google about, and whose Play Integrity section names the setting in logs.</param>
    /// <param name="logger">The logger for failures. It never logs the service account.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public GooglePlayIntegrityDecoder(IOptions<PlayIntegrityOptions> playIntegrity, AppAttestationSetup setup, ILogger<GooglePlayIntegrityDecoder> logger)
    {
        ArgumentNullException.ThrowIfNull(playIntegrity);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceAccountJson = playIntegrity.Value.ServiceAccountJson;
        _applicationName = playIntegrity.Value.ApplicationName;
        _packageName = setup.PackageName;
        _serviceAccountSetting = $"{setup.PlayIntegritySection}:{nameof(PlayIntegrityOptions.ServiceAccountJson)}";
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IntegrityVerdict> DecodeAsync(string integrityToken, CancellationToken ct = default)
    {
        var service = GetService();

        try
        {
            var request = service.V1.DecodeIntegrityToken(new DecodeIntegrityTokenRequest { IntegrityToken = integrityToken }, _packageName);
            var response = await request.ExecuteAsync(ct);
            var payload = response.TokenPayloadExternal
                ?? throw new AttestationUnavailableException("Google returned no token payload.");

            return new IntegrityVerdict(
                payload.RequestDetails?.RequestPackageName,
                payload.RequestDetails?.Nonce,
                payload.AppIntegrity?.AppRecognitionVerdict,
                payload.AppIntegrity?.PackageName,
                payload.AppIntegrity?.CertificateSha256Digest?.ToList() ?? [],
                payload.DeviceIntegrity?.DeviceRecognitionVerdict?.ToList() ?? []);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (AttestationUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Play Integrity decodeIntegrityToken failed ({Type})", ex.GetType().Name);
            throw new AttestationUnavailableException("Google's Play Integrity service did not answer.", ex);
        }
    }

    private PlayIntegrityService GetService()
    {
        lock (_gate)
        {
            if (_service is not null)
                return _service;

            if (string.IsNullOrWhiteSpace(_serviceAccountJson))
            {
                _logger.LogError("{Setting} is not configured: no app can be attested until it is set.", _serviceAccountSetting);
                throw new AttestationUnavailableException($"{_serviceAccountSetting} is not configured.");
            }

            try
            {
                var credential = CredentialFactory.FromJson<ServiceAccountCredential>(_serviceAccountJson)
                    .ToGoogleCredential()
                    .CreateScoped(PlayIntegrityService.Scope.Playintegrity);

                _service = new PlayIntegrityService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = _applicationName,
                });
                return _service;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Setting} is not a usable service account credential ({Type})", _serviceAccountSetting, ex.GetType().Name);
                throw new AttestationUnavailableException("The Play Integrity service account is not usable.", ex);
            }
        }
    }

    /// <summary>Releases the Google client, if one was built.</summary>
    public void Dispose() => _service?.Dispose();
}
