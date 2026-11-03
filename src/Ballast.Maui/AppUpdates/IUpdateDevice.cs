// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.AppUpdates;

/// <summary>The device facts the update check reads. The platform implementation answers from the operating system.</summary>
public interface IUpdateDevice
{
    /// <summary>The build number stamped into the assembly, or null when it is not there (the check is then skipped).</summary>
    int? StampedBuild { get; }

    /// <summary>The platform name the server is asked for (<c>android</c>, <c>ios</c> or <c>windows</c>), or null where there is no check.</summary>
    string? PlatformQueryName { get; }

    /// <summary>
    /// The build number the platform's own manifest carries, or null where the platform has none to compare with the stamp. The check is
    /// skipped when it differs from the stamp.
    /// </summary>
    string? ManifestBuildText { get; }
}
