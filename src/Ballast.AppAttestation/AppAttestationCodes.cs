// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>
/// The three error codes the library answers with. They are the wire that installed clients are written against, so a
/// released value is never renamed, reused or changed (CLAUDE.md rule 6). The app sets them in
/// <see cref="AppAttestationSetup"/>. These are the values the first consuming app shipped with, and a new app should use the
/// same spelling so that its clients can share one translation table.
/// </summary>
public static class AppAttestationCodes
{
    /// <summary>
    /// A data call carried no valid app session while <see cref="AppAttestationOptions.Enforce"/> is on: 401. The app
    /// attests again once and replays the call.
    /// </summary>
    public const string SessionRequired = "APP_SESSION_REQUIRED";

    /// <summary>
    /// The install was not vouched for: 403. The causes are a build Play does not recognise and that is not a listed
    /// developer certificate, a device that fails device integrity, a verdict for another package, or a nonce that is unknown,
    /// used or expired.
    /// </summary>
    public const string AttestationFailed = "APP_ATTESTATION_FAILED";

    /// <summary>
    /// The attestation could not be decided: 503. The causes are Google failing or not answering, no service account being
    /// configured, or a nonce or ticket store being full. It is never read as a pass.
    /// </summary>
    public const string AttestationUnavailable = "APP_ATTESTATION_UNAVAILABLE";
}
