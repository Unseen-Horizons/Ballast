// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>
/// The names the library uses that are part of the session token or the authentication scheme, and that no app chooses.
/// The names an app does choose (its header, cookie, issuer, audience, package and codes) are in
/// <see cref="AppAttestationSetup"/>.
/// </summary>
public static class AppAttestationDefaults
{
    /// <summary>The authentication scheme name. Use it in <c>[Authorize(AuthenticationSchemes = ...)]</c> on the ticket route.</summary>
    public const string SchemeName = "AppSession";

    /// <summary>The claim that holds a session's random id. It names a session, never a person.</summary>
    public const string SessionIdClaim = "sid";

    /// <summary>The claim that holds how the install was vouched for: <see cref="Attestation"/>.</summary>
    public const string AttestationClaim = "att";

    /// <summary>The claim that holds the platform the install reported.</summary>
    public const string PlatformClaim = "plat";

    /// <summary>The placeholder a secret is left at in a template. A signing key with this value stops startup.</summary>
    public const string PlaceholderSecret = "CHANGE-ME";

    /// <summary>The shortest signing key, in UTF-8 bytes.</summary>
    public const int MinimumKeyBytes = 32;
}
