// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>
/// The raw values of one platform's section, or of one channel's section when that channel has numbers of its own, as they are
/// configured. Nothing is parsed or checked here: <see cref="AppVersionManager"/> does that, and says what it finds.
/// </summary>
/// <param name="Label">The section's name below the configuration section: the platform (<c>Android</c>), or the platform and channel (<c>Android:direct</c>).</param>
/// <param name="Channel">The channel whose section answered, or null when the platform-wide section answered.</param>
/// <param name="LatestBuild">The configured <c>LatestBuild</c>, as text, or null.</param>
/// <param name="MinimumBuild">The configured <c>MinimumBuild</c>, as text, or null.</param>
/// <param name="LatestVersion">The configured <c>LatestVersion</c>, or null.</param>
/// <param name="StoreUrl">The configured <c>StoreUrl</c>, or null.</param>
/// <param name="Messages">The configured <c>Messages</c> by language code, without blank entries. It is empty when none are set.</param>
public sealed record ConfiguredAppVersion(
    string Label,
    string? Channel,
    string? LatestBuild,
    string? MinimumBuild,
    string? LatestVersion,
    string? StoreUrl,
    IReadOnlyDictionary<string, string> Messages);
