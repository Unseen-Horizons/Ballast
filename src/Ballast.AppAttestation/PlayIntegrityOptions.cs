// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>
/// The Google service account the server decodes integrity tokens with, bound from the configuration section named by
/// <see cref="AppAttestationSetup.PlayIntegritySection"/> (<c>PlayIntegrity</c> by default).
/// <para>
/// The service account is empty until the owner sets it (an App Service setting or a Key Vault secret, never a file in the
/// repository). Until then every attestation answers 503 <c>APP_ATTESTATION_UNAVAILABLE</c>, never a pass. The app's own Google
/// Cloud project number is app configuration, and this library does not read it.
/// </para>
/// </summary>
public sealed class PlayIntegrityOptions
{
    /// <summary>The application name the Google client reports, when the configuration does not set one: <c>Ballast.AppAttestation</c>.</summary>
    public const string DefaultApplicationName = "Ballast.AppAttestation";

    /// <summary>The service account's JSON key, as one string. Empty (the default) means no app can be attested.</summary>
    public string ServiceAccountJson { get; set; } = string.Empty;

    /// <summary>The application name the Google client reports. Default <see cref="DefaultApplicationName"/>.</summary>
    public string ApplicationName { get; set; } = DefaultApplicationName;
}
