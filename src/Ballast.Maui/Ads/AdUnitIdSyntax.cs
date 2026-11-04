// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Maui.Ads;

/// <summary>
/// The syntax of an AdMob ad unit id, banner or native. An id is <c>ca-app-pub-</c>, a 16-digit publisher id, a slash and a 10-digit
/// unit id. Anything looser accepts a typo that the AdMob SDK then rejects at run time, with no ad and no start-up error.
/// </summary>
public static partial class AdUnitIdSyntax
{
    [GeneratedRegex(@"^ca-app-pub-[0-9]{16}/[0-9]{10}\z", RegexOptions.CultureInvariant)]
    private static partial Regex UnitId();

    /// <summary>Whether <paramref name="id"/> is a banner unit id in the right form.</summary>
    /// <param name="id">The id to check. Null is not valid.</param>
    /// <returns>True for a well-formed id.</returns>
    public static bool IsValidBannerId(string? id) => id is not null && UnitId().IsMatch(id);

    /// <summary>Whether <paramref name="id"/> is a native unit id in the right form.</summary>
    /// <param name="id">The id to check. Null is not valid.</param>
    /// <returns>True for a well-formed id.</returns>
    public static bool IsValidNativeId(string? id) => id is not null && UnitId().IsMatch(id);

    /// <summary>
    /// The start-up problem with a release configuration, or null when it is fine. Ads switched off are always fine. Ads switched on
    /// need a well-formed banner id, because a build that quietly ships without its ads looks successful and is not. A native id is
    /// optional: empty means the native cards are off and the banner is unaffected, but a non-empty malformed one is the same mistake.
    /// </summary>
    /// <param name="enabled">The ads switch.</param>
    /// <param name="bannerId">The banner unit id.</param>
    /// <param name="nativeId">The native unit id, or null or empty for none.</param>
    /// <param name="sectionName">The settings section the switch and the ids come from, named in the message.</param>
    /// <returns>The message to fail the start with, or null.</returns>
    public static string? ReleaseConfigurationError(bool enabled, string? bannerId, string? nativeId = null, string sectionName = "Ads")
    {
        if (!enabled)
            return null;

        if (!IsValidBannerId(bannerId))
            return $"{sectionName}:Enabled is true, but {sectionName}:AndroidBannerAdUnitId is missing or not of the form "
                + "ca-app-pub-<16 digits>/<10 digits>. Set a valid ad unit id or set " + sectionName + ":Enabled to false.";

        if (!string.IsNullOrWhiteSpace(nativeId) && !IsValidNativeId(nativeId))
            return $"{sectionName}:Enabled is true, but {sectionName}:AndroidNativeAdUnitId is not of the form "
                + "ca-app-pub-<16 digits>/<10 digits>. Set a valid ad unit id or leave it empty to turn native ads off.";

        return null;
    }
}
