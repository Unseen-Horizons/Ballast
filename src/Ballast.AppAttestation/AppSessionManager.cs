// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Ballast.Errors;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation;

/// <summary>
/// The bootstrap of an app session: a nonce, an attested session, and the ticket that carries a session into the web view. It holds
/// the rules; the app's controller only passes values in and writes the answer. Every refusal is an <see cref="AppException"/> with a
/// stable code.
/// </summary>
public sealed class AppSessionManager
{
    /// <summary>
    /// The longest redirect path a ticket accepts. A map's own address carries the flagged stops, the filters and a route pick, which
    /// together can pass 2 KB; this only stops an absurd value.
    /// </summary>
    public const int MaxNextLength = 8192;

    private readonly AppAttestationSetup _setup;
    private readonly IAppSessionNonceStore _nonces;
    private readonly IAppSessionTicketStore _tickets;
    private readonly PlayIntegrityVerifier _verifier;
    private readonly AppSessionTokenService _tokens;
    private readonly AppSessionOptions _session;
    private readonly AppAttestationOptions _attestation;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _clock;
    private readonly ILogger<AppSessionManager> _logger;

    /// <summary>Creates the manager. The app registers it as scoped through <see cref="AppAttestationRegistration.AddAppAttestation"/>.</summary>
    /// <param name="setup">The setup: the platforms, the redirect prefix and the refusal codes.</param>
    /// <param name="nonces">The nonce store.</param>
    /// <param name="tickets">The web-ticket store.</param>
    /// <param name="verifier">The Play Integrity verifier.</param>
    /// <param name="tokens">The session signer.</param>
    /// <param name="session">The session options: the lifetimes.</param>
    /// <param name="attestation">The attestation options: <see cref="AppAttestationOptions.AllowUnattested"/> is read here.</param>
    /// <param name="environment">The host environment. The Development bypass is honoured only in Development.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">The logger for the audit trail and the refusals.</param>
    public AppSessionManager(
        AppAttestationSetup setup,
        IAppSessionNonceStore nonces,
        IAppSessionTicketStore tickets,
        PlayIntegrityVerifier verifier,
        AppSessionTokenService tokens,
        IOptions<AppSessionOptions> session,
        IOptions<AppAttestationOptions> attestation,
        IHostEnvironment environment,
        TimeProvider clock,
        ILogger<AppSessionManager> logger)
    {
        _setup = setup;
        _nonces = nonces;
        _tickets = tickets;
        _verifier = verifier;
        _tokens = tokens;
        _session = session.Value;
        _attestation = attestation.Value;
        _environment = environment;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Issues a single-use nonce, which the app requests its integrity token with.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The nonce.</returns>
    /// <exception cref="AppException">The nonce store is full: 503 with <see cref="AppAttestationSetup.AttestationUnavailableCode"/>.</exception>
    public async Task<AppNonceResponse> IssueNonceAsync(CancellationToken ct = default)
    {
        var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        if (!await _nonces.AddAsync(nonce, _session.NonceLifetime, ct))
            throw Busy();

        return new AppNonceResponse { Nonce = nonce };
    }

    /// <summary>
    /// Attests the install and signs it a session. The nonce is consumed first, whatever follows, so a failed attempt cannot be retried
    /// with the same one.
    /// <para>
    /// The Development bypass (<see cref="AppAttestationOptions.AllowUnattested"/>, ignored in every other environment) signs an
    /// unattested <c>none</c> session without reading the integrity token, so emulators and local runs need no Google. Otherwise a
    /// missing or refused token is 403 <see cref="AppAttestationSetup.AttestationFailedCode"/>, and a Google failure is 503
    /// <see cref="AppAttestationSetup.AttestationUnavailableCode"/>: never a session.
    /// </para>
    /// </summary>
    /// <param name="request">The install's request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The session token and its remaining lifetime.</returns>
    /// <exception cref="AppException">400 for a malformed request, 403 for a refused install, 503 when attestation cannot be decided.</exception>
    public async Task<AppSessionResponse> CreateSessionAsync(CreateAppSessionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var platform = request.Platform?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!_setup.Platforms.Contains(platform, StringComparer.Ordinal))
            throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Unknown platform.");
        if (string.IsNullOrWhiteSpace(request.InstallId) || request.InstallId.Length is < 8 or > 64)
            throw AppException.BadRequest(ErrorCodes.ValidationFailed, "InstallId must be 8 to 64 characters.");
        if (string.IsNullOrWhiteSpace(request.Nonce) || request.Nonce.Length > 128)
            throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Nonce is required.");

        if (!await _nonces.TakeAsync(request.Nonce, ct))
        {
            _logger.LogWarning("App session refused: unknown, used or expired nonce (platform {Platform}, install {Install})", platform, InstallHash(request.InstallId));
            throw Failed();
        }

        string attestation;
        if (_environment.IsDevelopment() && _attestation.AllowUnattested)
        {
            attestation = Attestation.None;
        }
        else if (string.IsNullOrWhiteSpace(request.IntegrityToken))
        {
            _logger.LogWarning("App session refused: no integrity token (platform {Platform}, install {Install})", platform, InstallHash(request.InstallId));
            throw Failed();
        }
        else
        {
            AttestationDecision decision;
            try
            {
                decision = await _verifier.VerifyAsync(request.IntegrityToken, request.Nonce, ct);
            }
            catch (AttestationUnavailableException ex)
            {
                _logger.LogError("App attestation unavailable ({Reason}); no session issued (platform {Platform}, install {Install})", ex.Message, platform, InstallHash(request.InstallId));
                throw new AppException(_setup.AttestationUnavailableCode, "App verification is unavailable right now. Try again shortly.", 503);
            }

            if (!decision.Accepted)
                throw Failed();
            attestation = decision.Attestation!;
        }

        var session = _tokens.Issue(attestation, platform);

        // The audit trail, and the way to spot an install that attests unusually often. Nothing is stored.
        _logger.LogInformation(
            "App session issued sid={Sid} att={Att} plat={Plat} install={Install}",
            session.SessionId, attestation, platform, InstallHash(request.InstallId));

        return new AppSessionResponse
        {
            AccessToken = session.Token,
            ExpiresInSeconds = (int)Math.Max(0, (session.ExpiresAt - _clock.GetUtcNow()).TotalSeconds),
        };
    }

    /// <summary>Issues a single-use ticket standing for the caller's own session, to put in the web view's address.</summary>
    /// <param name="session">The session the gate found on the request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The ticket.</returns>
    /// <exception cref="AppException">The ticket store is full: 503 with <see cref="AppAttestationSetup.AttestationUnavailableCode"/>.</exception>
    public async Task<AppWebTicketResponse> IssueWebTicketAsync(AppSessionFeature session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        if (!await _tickets.AddAsync(ticket, new WebTicketGrant(session.Token, session.Info.ExpiresAt), _session.TicketLifetime, ct))
            throw Busy();

        return new AppWebTicketResponse { Ticket = ticket };
    }

    /// <summary>
    /// Spends a ticket. <paramref name="next"/> is checked first (a relative path under <see cref="AppAttestationSetup.RedirectPrefix"/>,
    /// else 400) so the endpoint is never an open redirect, and a refused <c>next</c> spends nothing. A ticket that is unknown, used or
    /// expired yields the redirect with no cookie: the page loads, and if its data then needs a session it asks the app for a new one.
    /// </summary>
    /// <param name="ticket">The ticket from the address.</param>
    /// <param name="next">The path to redirect to, path and query.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>Where to send the browser, and the cookie's token when the ticket was redeemable.</returns>
    /// <exception cref="AppException">400 with <see cref="ErrorCodes.ValidationFailed"/> when <paramref name="next"/> is not a safe path.</exception>
    public async Task<WebTicketRedemption> RedeemWebTicketAsync(string? ticket, string? next, CancellationToken ct = default)
    {
        if (!IsSafeRedirectPath(next, _setup.RedirectPrefix))
            throw AppException.BadRequest(ErrorCodes.ValidationFailed, $"next must be a relative path under {_setup.RedirectPrefix}.");

        var grant = string.IsNullOrWhiteSpace(ticket) ? null : await _tickets.TakeAsync(ticket, ct);
        if (grant is null)
        {
            _logger.LogWarning("Web ticket not redeemed: unknown, used or expired; redirecting without a session cookie");
            return new WebTicketRedemption(next!, null, null, null);
        }

        var remaining = grant.ExpiresAt - _clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            return new WebTicketRedemption(next!, null, null, null);

        return new WebTicketRedemption(next!, grant.Token, grant.ExpiresAt, remaining);
    }

    /// <summary>
    /// True for a path and query that stays on this host under <paramref name="prefix"/>. An absolute URL, a protocol-relative
    /// <c>//host</c>, a backslash (a browser reads it as a slash), a control character, a space, a non-ASCII character or a <c>..</c>
    /// segment (also when encoded, in any case) is refused.
    /// </summary>
    /// <param name="next">The path to test.</param>
    /// <param name="prefix">The path prefix it must begin with, for example <c>/map/</c>.</param>
    /// <returns>True when the path is safe to redirect to.</returns>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is blank.</exception>
    public static bool IsSafeRedirectPath(string? next, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (string.IsNullOrEmpty(next) || next.Length > MaxNextLength)
            return false;
        if (!next.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        // Printable ASCII only: a control character, a space, DEL or any non-ASCII character is refused.
        if (next.Contains('\\') || next.Any(c => c < 0x21 || c > 0x7E))
            return false;

        // One decode, as a browser makes it, so %2e in any case and mixed with dots all name a dot segment.
        var path = next.Split('?', '#')[0];
        if (path.Split('/').Any(seg => Uri.UnescapeDataString(seg) == ".."))
            return false;

        return Uri.TryCreate(next, UriKind.Relative, out _);
    }

    private AppException Failed() =>
        new(_setup.AttestationFailedCode, "This app could not be verified.", 403);

    private AppException Busy() =>
        new(_setup.AttestationUnavailableCode, "App verification is busy. Try again shortly.", 503);

    private static string InstallHash(string installId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installId)))[..12].ToLowerInvariant();
}
