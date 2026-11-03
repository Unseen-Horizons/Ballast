// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>
/// The install channels the version answer knows, as they are named in the <c>channel</c> query and in configuration. A
/// build is installable only through the channel that carries it, so the update notice asks about the channel this install
/// came from. These names are the defaults of <see cref="AppUpdateOptions.Channels"/>.
/// </summary>
public static class InstallChannels
{
    /// <summary>Android installed by Google Play.</summary>
    public const string Play = "play";

    /// <summary>Installed outside any store: an APK, a development deploy, an unpackaged Windows build.</summary>
    public const string Direct = "direct";

    /// <summary>iOS, from the App Store (TestFlight counts as the same).</summary>
    public const string AppStore = "appstore";

    /// <summary>The installer package name Google Play reports for an app it installed.</summary>
    public const string PlayInstallerPackage = "com.android.vending";

    /// <summary>
    /// Android's channel for an installer package name: Play's own package is <see cref="Play"/>. Anything else (another
    /// store, adb, a file manager) and no installer at all are <see cref="Direct"/>.
    /// </summary>
    /// <param name="installerPackageName">The installer package name the platform reports, or null when it reports none.</param>
    /// <returns><see cref="Play"/> or <see cref="Direct"/>.</returns>
    public static string FromAndroidInstaller(string? installerPackageName) =>
        string.Equals(installerPackageName?.Trim(), PlayInstallerPackage, StringComparison.Ordinal) ? Play : Direct;

    /// <summary>
    /// True when the channel is a store listing, false for <see cref="Direct"/> (a download, not a store) and for null. It is
    /// only used for wording: "from the store you installed it from" is wrong for a download.
    /// </summary>
    /// <param name="channel">The channel, or null when it is not known.</param>
    /// <returns>True for <see cref="Play"/> and <see cref="AppStore"/>.</returns>
    public static bool IsStore(string? channel) => channel is Play or AppStore;
}
