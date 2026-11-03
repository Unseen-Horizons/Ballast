// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Platform;

/// <summary>
/// The install channels a version endpoint knows about. A build is installable only through the channel that carries it, so the update notice asks
/// about the channel this install came from.
/// </summary>
public static class InstallChannels
{
    /// <summary>Android installed by Google Play.</summary>
    public const string Play = "play";

    /// <summary>Installed outside any store: an APK, a development deploy, an unpackaged Windows build.</summary>
    public const string Direct = "direct";

    /// <summary>iOS (the App Store, and TestFlight, which counts as the same).</summary>
    public const string AppStore = "appstore";

    /// <summary>The installer package name Google Play reports for an app it installed.</summary>
    public const string PlayInstallerPackage = "com.android.vending";

    /// <summary>
    /// Android's channel for an installer package name: Play's own package is <see cref="Play"/>, anything else (another store, adb, a file manager)
    /// and no installer at all are <see cref="Direct"/>.
    /// </summary>
    /// <param name="installerPackageName">The installer's package name, or null when there is none.</param>
    /// <returns>The channel.</returns>
    public static string FromAndroidInstaller(string? installerPackageName) =>
        string.Equals(installerPackageName?.Trim(), PlayInstallerPackage, StringComparison.Ordinal) ? Play : Direct;

    /// <summary>
    /// True when the channel is a store listing, false for <see cref="Direct"/> (a download, not a store). Only used for wording: "from the store you
    /// installed it from" is wrong for a download.
    /// </summary>
    /// <param name="channel">The channel, or null.</param>
    /// <returns>True for a store channel.</returns>
    public static bool IsStore(string? channel) => channel is Play or AppStore;
}

/// <summary>
/// Where this install came from. The platform implementation asks the operating system, so the update notice can ask the server about the right
/// channel.
/// </summary>
public interface IInstallChannel
{
    /// <summary>
    /// One of <see cref="InstallChannels"/>, or null when it could not be told (the implementation logs why). A null channel is never replaced by a
    /// guess: the update check is skipped and shows nothing.
    /// </summary>
    string? Channel { get; }
}

/// <summary>A channel that is the same for every install of the platform (iOS, Windows).</summary>
public sealed class FixedInstallChannel : IInstallChannel
{
    /// <summary>Creates the channel.</summary>
    /// <param name="channel">The one channel every install of the platform reports.</param>
    public FixedInstallChannel(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        Channel = channel;
    }

    /// <inheritdoc />
    public string? Channel { get; }
}
