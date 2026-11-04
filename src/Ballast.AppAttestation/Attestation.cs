// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>How a session's install was vouched for. The value is written in the token's <c>att</c> claim.</summary>
public static class Attestation
{
    /// <summary>Google Play recognised the app and the device is genuine.</summary>
    public const string Play = "play";

    /// <summary>
    /// The owner's own build: signed with a developer certificate listed in configuration, on a genuine device, but not
    /// recognised by Play (a sideload from an IDE).
    /// </summary>
    public const string Developer = "dev";

    /// <summary>Nothing was checked. Issued only by the Development bypass (<see cref="AppAttestationOptions.AllowUnattested"/>).</summary>
    public const string None = "none";

    /// <summary>True for one of the three values this library issues.</summary>
    /// <param name="value">The value to test, or null.</param>
    /// <returns>True when it is <see cref="Play"/>, <see cref="Developer"/> or <see cref="None"/>.</returns>
    public static bool IsKnown(string? value) => value is Play or Developer or None;
}
