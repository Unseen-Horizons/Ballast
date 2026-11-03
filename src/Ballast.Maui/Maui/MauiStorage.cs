// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Helpers;
using Ballast.Maui.Http;
using Ballast.Maui.Platform;
using Ballast.Maui.Storage;

namespace Ballast.Maui.Adapters;

/// <summary>The platform's preferences (MAUI Preferences), behind <see cref="IPreferenceStore"/>.</summary>
public sealed class MauiPreferences : IPreferenceStore
{
    /// <inheritdoc />
    public T Get<T>(string key, T defaultValue, string? sharedName) =>
        Microsoft.Maui.Storage.Preferences.Default.Get(key, defaultValue, sharedName);

    /// <inheritdoc />
    public void Set<T>(string key, T value, string? sharedName) =>
        Microsoft.Maui.Storage.Preferences.Default.Set(key, value, sharedName);

    /// <inheritdoc />
    public void Remove(string key, string? sharedName) =>
        Microsoft.Maui.Storage.Preferences.Default.Remove(key, sharedName);
}

/// <summary>The platform's secure store (MAUI SecureStorage), behind <see cref="ISecureValues"/>.</summary>
public sealed class MauiSecureValues : ISecureValues
{
    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await Microsoft.Maui.Storage.SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Microsoft.Maui.Storage.SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool Remove(string key) => Microsoft.Maui.Storage.SecureStorage.Default.Remove(key);

    /// <inheritdoc />
    public void RemoveAll() => Microsoft.Maui.Storage.SecureStorage.Default.RemoveAll();
}

/// <summary>The platform's data and cache folders (MAUI FileSystem), behind <see cref="IAppPaths"/>.</summary>
public sealed class MauiAppPaths : IAppPaths
{
    /// <inheritdoc />
    public string AppDataDirectory => Microsoft.Maui.Storage.FileSystem.AppDataDirectory;

    /// <inheritdoc />
    public string CacheDirectory => Microsoft.Maui.Storage.FileSystem.CacheDirectory;
}

/// <summary>The app's own packaged files (MAUI FileSystem's app package), behind <see cref="IAppPackage"/>.</summary>
public sealed class MauiAppPackage : IAppPackage
{
    /// <inheritdoc />
    public Task<Stream> OpenAsync(string path) => Microsoft.Maui.Storage.FileSystem.OpenAppPackageFileAsync(path);
}

/// <summary>What the app and the device say about themselves (MAUI AppInfo and DeviceInfo), behind <see cref="IAppBuildInfo"/>.</summary>
public sealed class MauiAppInfo : IAppBuildInfo
{
    /// <inheritdoc />
    public string VersionString => Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;

    /// <inheritdoc />
    public string BuildString => Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString;

    /// <inheritdoc />
    public string PlatformName => Microsoft.Maui.Devices.DeviceInfo.Current.Platform.ToString();

    /// <inheritdoc />
    public string OsVersion => Microsoft.Maui.Devices.DeviceInfo.Current.VersionString;
}

/// <summary>
/// Whether this installed build is debuggable. On Android the flag is read from the application info. Other platforms do not report it, so they
/// answer null, and the certificate option is not refused there (see <see cref="IRuntimeBuild"/>).
/// </summary>
public sealed class MauiRuntimeBuild : IRuntimeBuild
{
    /// <inheritdoc />
    public bool? IsDebuggable
    {
        get
        {
#if ANDROID
            var flags = Android.App.Application.Context.ApplicationInfo?.Flags ?? Android.Content.PM.ApplicationInfoFlags.None;
            return (flags & Android.Content.PM.ApplicationInfoFlags.Debuggable) != 0;
#else
            return null;
#endif
        }
    }
}
