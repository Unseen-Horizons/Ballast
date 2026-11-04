// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation;

/// <summary>
/// Says at start-up what the configuration will do, so a missing setting is read off the log instead of found out when the first
/// install cannot open the app. It never stops the start: the one thing that does stop it is the signing key, checked at registration.
/// </summary>
public sealed class AppAttestationStartupCheck : IHostedService
{
    private readonly IOptions<AppAttestationOptions> _attestation;
    private readonly IOptions<PlayIntegrityOptions> _playIntegrity;
    private readonly AppAttestationSetup _setup;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AppAttestationStartupCheck> _logger;

    /// <summary>Creates the check. The host starts it.</summary>
    /// <param name="attestation">The attestation options.</param>
    /// <param name="playIntegrity">The Google settings.</param>
    /// <param name="setup">The setup, which names the configuration keys in the messages.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="logger">The logger.</param>
    public AppAttestationStartupCheck(
        IOptions<AppAttestationOptions> attestation,
        IOptions<PlayIntegrityOptions> playIntegrity,
        AppAttestationSetup setup,
        IHostEnvironment environment,
        ILogger<AppAttestationStartupCheck> logger)
    {
        _attestation = attestation;
        _playIntegrity = playIntegrity;
        _setup = setup;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>Logs what the configuration does: the enforcement mode, the bypass, the developer certificates and the service account.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var attestation = _attestation.Value;
        var allowUnattestedKey = $"{_setup.AttestationSection}:{nameof(AppAttestationOptions.AllowUnattested)}";

        if (attestation.AllowUnattested && !_environment.IsDevelopment())
            _logger.LogError(
                "{Key} is set in the {Environment} environment. It is ignored here: unattested sessions are only ever issued in Development. Remove the setting.",
                allowUnattestedKey, _environment.EnvironmentName);

        _ = attestation.NormalizedDeveloperDigests(out var rejected);
        foreach (var entry in rejected)
            _logger.LogError(
                "{Section}:{Key} has an entry that is not a SHA-256 digest and will never match: '{Entry}'. Use the hex from keytool or the base64url from Play Integrity.",
                _setup.AttestationSection, nameof(AppAttestationOptions.DeveloperCertificates), entry);

        if (!_environment.IsDevelopment() && string.IsNullOrWhiteSpace(_playIntegrity.Value.ServiceAccountJson))
            _logger.LogWarning(
                "{Section}:{Key} is not set: no app can be attested, and the session request answers 503 until it is.",
                _setup.PlayIntegritySection, nameof(PlayIntegrityOptions.ServiceAccountJson));

        _logger.LogInformation(
            "App session gate: {Mode}. Developer certificates configured: {Count}.",
            attestation.Enforce ? "ENFORCED (a data call with no session is refused with 401)" : "log-only (a data call with no session is allowed and logged)",
            attestation.NormalizedDeveloperDigests(out _).Count);

        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
