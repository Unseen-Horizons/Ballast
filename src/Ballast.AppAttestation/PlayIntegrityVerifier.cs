// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Text;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation;

/// <summary>
/// The fields of a decoded Play Integrity verdict that the server looks at. Google's own response type is mapped into this by
/// <see cref="GooglePlayIntegrityDecoder"/>, so the verifier is tested on plain fixtures and never reaches Google.
/// </summary>
/// <param name="RequestPackageName"><c>requestDetails.requestPackageName</c>.</param>
/// <param name="Nonce"><c>requestDetails.nonce</c>, base64url, as the app passed it.</param>
/// <param name="AppRecognitionVerdict"><c>PLAY_RECOGNIZED</c>, <c>UNRECOGNIZED_VERSION</c> or <c>UNEVALUATED</c>.</param>
/// <param name="PackageName"><c>appIntegrity.packageName</c>. Google populates it unless the verdict is <c>UNEVALUATED</c>.</param>
/// <param name="CertificateSha256Digests"><c>appIntegrity.certificateSha256Digest</c>, base64url. Populated whenever the verdict is not <c>UNEVALUATED</c>.</param>
/// <param name="DeviceRecognitionVerdicts"><c>deviceIntegrity.deviceRecognitionVerdict</c>.</param>
public sealed record IntegrityVerdict(
    string? RequestPackageName,
    string? Nonce,
    string? AppRecognitionVerdict,
    string? PackageName,
    IReadOnlyList<string> CertificateSha256Digests,
    IReadOnlyList<string> DeviceRecognitionVerdicts);

/// <summary>Decodes an integrity token into its verdict. The real one calls Google; tests supply fixtures.</summary>
public interface IPlayIntegrityDecoder
{
    /// <summary>Decodes a token.</summary>
    /// <param name="integrityToken">The token the app sent.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="AttestationUnavailableException">The answer could not be had. It is never read as a pass.</exception>
    Task<IntegrityVerdict> DecodeAsync(string integrityToken, CancellationToken ct = default);
}

/// <summary>
/// The attestation could not be decided: Google failed, was not reachable, or the service account is not configured. It is
/// always a 503, and it is never read as a pass.
/// </summary>
public sealed class AttestationUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What failed, for a log line.</param>
    public AttestationUnavailableException(string message) : base(message) { }

    /// <summary>Creates the exception with the failure that caused it.</summary>
    /// <param name="message">What failed, for a log line.</param>
    /// <param name="inner">The failure.</param>
    public AttestationUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// What the verifier decided. <see cref="Attestation"/> is set when the install is vouched for; otherwise <see cref="Refusal"/>
/// says why, for the log (the caller is only told it was refused).
/// </summary>
/// <param name="Attestation">The attestation value, or null when refused.</param>
/// <param name="Refusal">The reason for a refusal, or null when accepted.</param>
public sealed record AttestationDecision(string? Attestation, string? Refusal)
{
    /// <summary>True when the install is vouched for.</summary>
    public bool Accepted => Attestation is not null;

    /// <summary>An accepted install with its attestation value.</summary>
    /// <param name="attestation">The attestation value.</param>
    /// <returns>The decision.</returns>
    public static AttestationDecision Accept(string attestation) => new(attestation, null);

    /// <summary>A refused install with its reason.</summary>
    /// <param name="reason">The reason, for the log.</param>
    /// <returns>The decision.</returns>
    public static AttestationDecision Refuse(string reason) => new(null, reason);
}

/// <summary>
/// Decides, from a decoded verdict, whether an install may have a session.
/// <para>
/// Accepted when the verdict names the app's package in both places, answers the nonce the server issued, the device meets
/// device integrity, and either
/// </para>
/// <list type="bullet">
/// <item>Play recognises the app (<c>PLAY_RECOGNIZED</c>): attestation <c>play</c>; or</item>
/// <item>
/// <b>the developer path:</b> Play does not (<c>UNRECOGNIZED_VERSION</c>, which is what a sideload from an IDE gets) and every
/// certificate digest in the verdict is one the owner listed in <see cref="AppAttestationOptions.DeveloperCertificates"/>:
/// attestation <c>dev</c>. Google populates <c>certificateSha256Digest</c> for any verdict other than <c>UNEVALUATED</c>
/// (https://developer.android.com/google/play/integrity/verdicts), so the digest is there to compare.
/// </item>
/// </list>
/// Anything else is a refusal. A failed call to Google is not a refusal and not a pass: it throws.
/// </summary>
public sealed class PlayIntegrityVerifier
{
    /// <summary>The verdict for an app Play recognises.</summary>
    public const string PlayRecognized = "PLAY_RECOGNIZED";

    /// <summary>The verdict for an app Play does not recognise, which includes a sideload and a package name Play does not record.</summary>
    public const string UnrecognizedVersion = "UNRECOGNIZED_VERSION";

    /// <summary>The device verdict that a genuine device earns.</summary>
    public const string MeetsDeviceIntegrity = "MEETS_DEVICE_INTEGRITY";

    private readonly IPlayIntegrityDecoder _decoder;
    private readonly ILogger<PlayIntegrityVerifier> _logger;
    private readonly string _packageName;
    private readonly HashSet<string> _developerDigests;

    /// <summary>Creates the verifier.</summary>
    /// <param name="decoder">The decoder: Google's in the app, a fake in tests.</param>
    /// <param name="options">The attestation options, whose developer certificates are read here.</param>
    /// <param name="setup">The setup, whose package name a verdict must name.</param>
    /// <param name="logger">The logger for refusals.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public PlayIntegrityVerifier(IPlayIntegrityDecoder decoder, IOptions<AppAttestationOptions> options, AppAttestationSetup setup, ILogger<PlayIntegrityVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(logger);

        _decoder = decoder;
        _logger = logger;
        _packageName = setup.PackageName;
        _developerDigests = options.Value.NormalizedDeveloperDigests(out _);
    }

    /// <summary>Decodes a token and decides whether its install may have a session.</summary>
    /// <param name="integrityToken">The token the app sent.</param>
    /// <param name="expectedNonce">The nonce the server issued for this request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The decision. A refusal is logged with the verdict's own fields, never the token.</returns>
    /// <exception cref="AttestationUnavailableException">Google could not answer. There is no decision.</exception>
    public async Task<AttestationDecision> VerifyAsync(string integrityToken, string expectedNonce, CancellationToken ct = default)
    {
        var verdict = await _decoder.DecodeAsync(integrityToken, ct);
        var decision = Decide(verdict, expectedNonce);

        // The verdict's own fields, never the token: what to look at when a rider or the owner's own build is refused.
        if (!decision.Accepted)
            _logger.LogWarning(
                "App attestation refused: {Reason}. appRecognitionVerdict={AppVerdict} device=[{Device}] package={Package} requestPackage={RequestPackage} certificates=[{Certificates}] nonceChars={VerdictNonceChars}/{ExpectedNonceChars}",
                decision.Refusal, verdict.AppRecognitionVerdict ?? "absent", string.Join(",", verdict.DeviceRecognitionVerdicts),
                verdict.PackageName ?? "absent", verdict.RequestPackageName ?? "absent", string.Join(",", verdict.CertificateSha256Digests),
                verdict.Nonce?.Length ?? 0, expectedNonce.Length);

        return decision;
    }

    private AttestationDecision Decide(IntegrityVerdict verdict, string expectedNonce)
    {
        if (!string.Equals(verdict.RequestPackageName, _packageName, StringComparison.Ordinal))
            return AttestationDecision.Refuse("the token was requested by another package");

        // The nonce is what ties this token to this server's request: a token captured elsewhere answers another nonce. Compared as
        // bytes, not text: the server issues unpadded base64url, and Google echoes the nonce in its own spelling (seen in production
        // 2026-10-05: the same nonce refused as "another nonce"). Constant-time, because it is a secret-shaped comparison.
        if (!NoncesMatch(verdict.Nonce, expectedNonce))
            return AttestationDecision.Refuse("the token answers another nonce");

        if (!verdict.DeviceRecognitionVerdicts.Contains(MeetsDeviceIntegrity, StringComparer.Ordinal))
            return AttestationDecision.Refuse("the device does not meet device integrity");

        // UNRECOGNIZED_VERSION also covers "the package name does not match Google Play records", so the package is checked here
        // for both paths and not only inferred from the verdict.
        if (!string.Equals(verdict.PackageName, _packageName, StringComparison.Ordinal))
            return AttestationDecision.Refuse("the verdict names another package");

        switch (verdict.AppRecognitionVerdict)
        {
            case PlayRecognized:
                return AttestationDecision.Accept(Attestation.Play);

            case UnrecognizedVersion:
                if (_developerDigests.Count == 0)
                    return AttestationDecision.Refuse("a build Play does not recognise, and no developer certificate is configured");
                if (verdict.CertificateSha256Digests.Count == 0)
                    return AttestationDecision.Refuse("a build Play does not recognise, and the verdict names no certificate");

                // Google says base64url, and does not say whether it is padded. The configured digests are normalised, so the verdict's
                // are too, or the owner's own sideload is refused over spelling. One that does not normalise is not a developer
                // certificate: a refusal, never a skip.
                return verdict.CertificateSha256Digests.All(IsDeveloperCertificate)
                    ? AttestationDecision.Accept(Attestation.Developer)
                    : AttestationDecision.Refuse("a build Play does not recognise, signed with a certificate that is not a developer certificate");

            default:
                return AttestationDecision.Refuse($"app recognition verdict is {verdict.AppRecognitionVerdict ?? "absent"}");
        }
    }

    private bool IsDeveloperCertificate(string digest) =>
        AppAttestationOptions.NormalizeDigest(digest) is { } normalised && _developerDigests.Contains(normalised);

    /// <summary>
    /// True when both nonces decode to the same bytes, whatever their base64 spelling: padded or not, URL-safe (<c>-</c> <c>_</c>) or
    /// standard (<c>+</c> <c>/</c>). A nonce that does not decode never matches.
    /// </summary>
    internal static bool NoncesMatch(string? fromVerdict, string expected)
    {
        var a = DecodeNonce(fromVerdict);
        var b = DecodeNonce(expected);
        return a is not null && b is not null && a.Length > 0 && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static byte[]? DecodeNonce(string? nonce)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            return null;

        var text = nonce.Trim().TrimEnd('=').Replace('+', '-').Replace('/', '_');
        try
        {
            return Base64Url.DecodeFromChars(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
