// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>What Google vouches for in a verified ID token.</summary>
/// <param name="Subject">Google's stable id for the person. The login is keyed by it, never by the address.</param>
/// <param name="Email">The Google address.</param>
/// <param name="EmailVerified">Whether Google has verified the address. An unverified address creates or links nothing.</param>
/// <param name="Name">The display name, or null.</param>
public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name);

/// <summary>
/// Checks a Google ID token. It is an interface so an app's tests, or an app with another check, can answer for Google. The library's
/// implementation is <see cref="GoogleIdTokenValidator"/>.
/// </summary>
public interface IGoogleIdTokenValidator
{
    /// <summary>Returns the identity the token proves, or null when the token is not a valid Google token for this app.</summary>
    /// <param name="idToken">The ID token the app's client obtained from Google.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The identity, or null for a token that is refused.</returns>
    /// <exception cref="Ballast.Errors.AppException">
    /// Google sign-in is not configured (<see cref="AuthFailures.GoogleSignInNotConfigured"/>), or Google could not be reached to check the
    /// token (<see cref="AuthFailures.GoogleUnreachable"/>). Neither is a bad token.
    /// </exception>
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default);
}
