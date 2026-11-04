// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Maui.AppAttestation;

/// <summary>What the Play Integrity attestation needs from the app.</summary>
/// <param name="CloudProjectNumber">
/// The Google Cloud project number, as the app's configuration gives it (text or a number). It lets Play Integrity answer a build that was
/// not installed from Play. Optional for a Play install whose app is linked to the project in Play Console. Null or blank is valid: the
/// server then refuses what only the project could have vouched for.
/// </param>
public sealed record PlayIntegrityOptions(string? CloudProjectNumber)
{
    /// <summary>The project number as a positive whole number, or null when it is blank, not a number, or zero.</summary>
    /// <param name="text">The configured text.</param>
    public static long? ParseProjectNumber(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
}
