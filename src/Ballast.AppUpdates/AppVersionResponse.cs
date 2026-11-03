// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>
/// What the server says about the app's builds for one platform and install channel (<c>GET app/version</c>). Its JSON names are
/// <c>platform</c>, <c>latestBuild</c>, <c>minimumBuild</c>, <c>latestVersion</c>, <c>storeUrl</c>, <c>channel</c> and <c>messages</c>,
/// and installed apps read them, so they do not change.
/// <para>
/// Builds are compared as integers, the same number as the Android <c>versionCode</c> and the app's build number. Display strings
/// such as "0.1.0" do not order reliably, and the build number is what the store orders by, so <see cref="LatestVersion"/> is only
/// text for the banner.
/// </para>
/// </summary>
public class AppVersionResponse
{
    /// <summary>The platform's name as configured (for example <c>Android</c>), not the request's spelling.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// The newest build that is published. A build below it is offered an update. 0 when only a minimum is configured (a store
    /// channel, where the store itself says whether a newer build exists).
    /// </summary>
    public int LatestBuild { get; set; }

    /// <summary>The oldest build still supported; 0 means there is no minimum. A build below it is blocked.</summary>
    public int MinimumBuild { get; set; }

    /// <summary>Display text for the banner ("0.2.0"); null when the server was not given one.</summary>
    public string? LatestVersion { get; set; }

    /// <summary>
    /// Where this install channel gets the build, as an absolute https link, or null. For a store channel it is a store listing; for
    /// a direct download it is a download link. A configured link that is not absolute https is dropped, never passed on.
    /// </summary>
    public string? StoreUrl { get; set; }

    /// <summary>
    /// The install channel whose configuration answered, or null when the platform-wide section answered (no channel was asked for,
    /// or that channel has no numbers of its own).
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>Optional extra text by language code ("en", "hr"). A language with no entry has no message.</summary>
    public Dictionary<string, string> Messages { get; set; } = [];
}
