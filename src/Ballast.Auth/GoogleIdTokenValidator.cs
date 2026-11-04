// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Google.Apis.Auth;

using Microsoft.Extensions.Logging;

namespace Ballast.Auth;

/// <summary>
/// Checks a Google ID token with Google's library: the signature against Google's published keys, the issuer, the expiry, and the audience
/// (the app's Web client id). Holds no database, so it is a singleton.
/// </summary>
public sealed class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    private static readonly string[] GoogleIssuers = ["accounts.google.com", "https://accounts.google.com"];

    private readonly AuthOptions _auth;
    private readonly AuthGoogleOptions _google;
    private readonly IGoogleTokenVerifier _verifier;
    private readonly ILogger<GoogleIdTokenValidator> _logger;

    /// <summary>Creates the validator.</summary>
    /// <param name="auth">The sign-in options, whose <see cref="AuthOptions.Failures"/> give the refusals.</param>
    /// <param name="google">The Google settings, with the Web client id.</param>
    /// <param name="verifier">Checks the token's signature with Google's keys. The default is <see cref="GoogleJsonWebSignatureVerifier"/>.</param>
    /// <param name="logger">The logger. It logs the reason a token was refused, never the token.</param>
    public GoogleIdTokenValidator(AuthOptions auth, AuthGoogleOptions google, IGoogleTokenVerifier verifier, ILogger<GoogleIdTokenValidator> logger)
    {
        _auth = auth;
        _google = google;
        _verifier = verifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(idToken);

        var clientId = _google.WebClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw _auth.Failures.GoogleSignInNotConfigured.ToException();
        }

        try
        {
            var payload = await _verifier.VerifyAsync(idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });

            if (string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
            {
                _logger.LogInformation("Google ID token rejected: no subject or email in the payload.");
                return null;
            }

            // The checks Google's library makes are made here too, on what came back, so a token from another issuer, for another app,
            // or past its expiry is refused whatever verifier answered.
            if (payload.Issuer is null || !GoogleIssuers.Contains(payload.Issuer, StringComparer.Ordinal))
            {
                _logger.LogInformation("Google ID token rejected: it was not issued by Google.");
                return null;
            }

            if (payload.AudienceAsList is null || !payload.AudienceAsList.Contains(clientId, StringComparer.Ordinal))
            {
                _logger.LogInformation("Google ID token rejected: it is not for this app.");
                return null;
            }

            if (payload.ExpirationTimeSeconds is not { } expires || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                _logger.LogInformation("Google ID token rejected: it has expired.");
                return null;
            }

            return new GoogleIdentity(payload.Subject, payload.Email, payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException ex)
        {
            // The reason, never the token.
            _logger.LogInformation("Google ID token rejected: {Reason}", ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Fetching Google's signing keys failed. That is a network problem, not a bad token.
            _logger.LogWarning(ex, "Could not reach Google to validate an ID token.");
            throw _auth.Failures.GoogleUnreachable.ToException();
        }
    }
}
