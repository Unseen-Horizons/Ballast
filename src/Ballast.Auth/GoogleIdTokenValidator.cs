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
    private readonly AuthOptions _auth;
    private readonly AuthGoogleOptions _google;
    private readonly ILogger<GoogleIdTokenValidator> _logger;

    /// <summary>Creates the validator.</summary>
    /// <param name="auth">The sign-in options, whose <see cref="AuthOptions.Failures"/> give the refusals.</param>
    /// <param name="google">The Google settings, with the Web client id.</param>
    /// <param name="logger">The logger. It logs the reason a token was refused, never the token.</param>
    public GoogleIdTokenValidator(AuthOptions auth, AuthGoogleOptions google, ILogger<GoogleIdTokenValidator> logger)
    {
        _auth = auth;
        _google = google;
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
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });

            if (string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
            {
                _logger.LogInformation("Google ID token rejected: no subject or email in the payload.");
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
