// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Ads;

/// <summary>
/// The log lines for the consent SDK's answers. A release build shows nothing else about why the consent form did not appear, so each
/// answer is logged as the SDK gave it, and a missing value is said to be missing.
/// </summary>
public static class ConsentLog
{
    /// <summary>The line for an updated consent answer.</summary>
    /// <param name="status">The SDK's status, as text, or null.</param>
    /// <param name="canRequestAds">Whether ads may be requested, or null when the SDK gave no answer.</param>
    /// <returns>The line.</returns>
    public static string InfoUpdated(string? status, bool? canRequestAds) =>
        $"[Ads] Consent information updated: status {Describe(status)}, ads may be requested: {canRequestAds switch
        {
            true => "yes",
            false => "no",
            null => "not given",
        }}";

    /// <summary>The line for a consent update that failed.</summary>
    /// <param name="message">The SDK's message, or null.</param>
    /// <returns>The line.</returns>
    public static string InfoFailedToUpdate(string? message) =>
        $"[Ads] Consent information could not be updated: {Describe(message)}";

    /// <summary>The line for a consent form that could not be shown.</summary>
    /// <param name="message">The SDK's message, or null.</param>
    /// <returns>The line.</returns>
    public static string FormError(string? message) =>
        $"[Ads] Consent form could not be shown: {Describe(message)}";

    private static string Describe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "not given by the SDK" : value.Trim();
}
