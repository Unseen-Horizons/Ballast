// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Reflection;

using Ballast.Maui.Localization;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace Ballast.Maui.AppUpdates;

/// <summary>Opens a link through the device's launcher, on the UI thread.</summary>
public sealed class MauiStoreLinkLauncher : IStoreLinkLauncher
{
    /// <inheritdoc />
    public Task<bool> OpenAsync(Uri uri) => MainThread.InvokeOnMainThreadAsync(() => Launcher.Default.OpenAsync(uri));
}

/// <summary>The update prompts as alerts on the app's first page. The words come from the app's localisation through the keys it gives.</summary>
public sealed class MauiUpdatePrompts : IUpdatePrompts
{
    private readonly LocalizationService _text;
    private readonly UpdateTextKeys _keys;

    /// <summary>Creates the prompts.</summary>
    /// <param name="text">The app's localised strings.</param>
    /// <param name="keys">The keys of the wording, or null for the defaults.</param>
    public MauiUpdatePrompts(LocalizationService text, UpdateTextKeys? keys = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _keys = keys ?? new UpdateTextKeys();
    }

    /// <inheritdoc />
    public async Task<bool> AskToRestartAsync()
    {
        try
        {
            if (CurrentPage() is not { } page)
            {
                Trace.WriteLine("[PlayUpdate] The update is downloaded, but there was no page to ask about restarting.");
                return false;
            }

            return await MainThread.InvokeOnMainThreadAsync(() =>
                page.DisplayAlertAsync(_text[_keys.DownloadedTitle], _text[_keys.DownloadedBody], _text[_keys.Restart], _text[_keys.Later]));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PlayUpdate] Asking to restart failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ShowStoreNotOpenedAsync(bool direct)
    {
        try
        {
            if (CurrentPage() is not { } page)
                return;

            var title = direct ? _keys.DownloadFailedTitle : _keys.StoreFailedTitle;
            var body = direct ? _keys.DownloadFailedBody : _keys.StoreFailedBody;
            await MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlertAsync(_text[title], _text[body], _text[_keys.Ok]));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppUpdater] Could not show the store failure: {ex.Message}");
        }
    }

    private static Page? CurrentPage() => Application.Current is { Windows.Count: > 0 } app ? app.Windows[0].Page : null;
}

/// <summary>
/// The device facts of the update check, read from the operating system. The build number is stamped into the assembly under the
/// metadata key the constructor is given; the platform's manifest build text is compared with it on Android and iOS.
/// </summary>
public sealed class MauiUpdateDevice : IUpdateDevice
{
    private readonly int? _stampedBuild;

    /// <summary>Creates the device facts.</summary>
    /// <param name="stampAssembly">The assembly that carries the build stamp (the app's).</param>
    /// <param name="metadataKey">The metadata key the project file stamps the build under.</param>
    public MauiUpdateDevice(Assembly stampAssembly, string metadataKey)
    {
        ArgumentNullException.ThrowIfNull(stampAssembly);
        _stampedBuild = AppBuildStamp.FromAttributes(stampAssembly.GetCustomAttributes<AssemblyMetadataAttribute>(), metadataKey);
    }

    /// <inheritdoc />
    public int? StampedBuild => _stampedBuild;

    /// <inheritdoc />
    public string? PlatformQueryName => DeviceInfo.Current.Platform switch
    {
        var platform when platform == DevicePlatform.Android => "android",
        var platform when platform == DevicePlatform.iOS => "ios",

        // Windows reads its build number from the stamp, not AppInfo.BuildString, which is the assembly version (0.1.0.0) on an unpackaged build.
        // Mac Catalyst has no channel, so it is skipped with a log line rather than compared against the wrong number.
        var platform when platform == DevicePlatform.WinUI => "windows",
        _ => null,
    };

    /// <inheritdoc />
    public string? ManifestBuildText =>
        DeviceInfo.Current.Platform == DevicePlatform.Android || DeviceInfo.Current.Platform == DevicePlatform.iOS
            ? AppInfo.Current.BuildString
            : null;
}
