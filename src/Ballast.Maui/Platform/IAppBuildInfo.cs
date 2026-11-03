// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Platform;

/// <summary>
/// What the app and the device say about themselves: the version and build of this app, the platform, and the operating system's
/// version. Each value is read on demand, and a read that the platform cannot answer throws, which each caller turns into "unknown".
/// </summary>
public interface IAppBuildInfo
{
    /// <summary>The version name the app shows, for example <c>0.9.0</c>.</summary>
    string VersionString { get; }

    /// <summary>The build number, as text.</summary>
    string BuildString { get; }

    /// <summary>The platform's name, for example <c>Android</c>.</summary>
    string PlatformName { get; }

    /// <summary>The operating system's version, as text.</summary>
    string OsVersion { get; }
}
