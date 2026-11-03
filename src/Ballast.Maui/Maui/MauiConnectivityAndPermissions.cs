// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Connectivity;
using Ballast.Maui.Permissions;

namespace Ballast.Maui.Adapters;

/// <summary>The platform's connectivity (MAUI Connectivity), behind <see cref="IConnectivitySource"/>. Create one for the app's life.</summary>
public sealed class MauiConnectivitySource : IConnectivitySource, IDisposable
{
    /// <summary>Subscribes to the platform's connectivity changes.</summary>
    public MauiConnectivitySource()
    {
        Microsoft.Maui.Networking.Connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    /// <inheritdoc />
    public event EventHandler<bool>? Changed;

    /// <inheritdoc />
    public bool IsOfflineNow() =>
        Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess != Microsoft.Maui.Networking.NetworkAccess.Internet;

    /// <inheritdoc />
    public bool IsOnCellular() =>
        Microsoft.Maui.Networking.Connectivity.Current.ConnectionProfiles.Contains(Microsoft.Maui.Networking.ConnectionProfile.Cellular);

    /// <summary>Drops the platform subscription.</summary>
    public void Dispose() => Microsoft.Maui.Networking.Connectivity.ConnectivityChanged -= OnConnectivityChanged;

    private void OnConnectivityChanged(object? sender, Microsoft.Maui.Networking.ConnectivityChangedEventArgs e) =>
        Changed?.Invoke(this, e.NetworkAccess != Microsoft.Maui.Networking.NetworkAccess.Internet);
}

/// <summary>
/// The platform's location permission (MAUI Permissions), behind <see cref="IPermissionSource"/>. Every call runs on the UI thread.
/// </summary>
public sealed class MauiPermissionSource : IPermissionSource
{
    /// <summary>The platform family the permission rules read, from the device.</summary>
    public static PermissionPlatform CurrentPlatform =>
        Microsoft.Maui.Devices.DeviceInfo.Platform == Microsoft.Maui.Devices.DevicePlatform.Android ? PermissionPlatform.Android
        : Microsoft.Maui.Devices.DeviceInfo.Platform == Microsoft.Maui.Devices.DevicePlatform.iOS ? PermissionPlatform.Ios
        : Microsoft.Maui.Devices.DeviceInfo.Platform == Microsoft.Maui.Devices.DevicePlatform.WinUI ? PermissionPlatform.Windows
        : PermissionPlatform.Other;

    /// <inheritdoc />
    public Task<AppPermissionStatus> CheckLocationAsync() =>
        Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(async () =>
            Map(await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<Microsoft.Maui.ApplicationModel.Permissions.LocationWhenInUse>()));

    /// <inheritdoc />
    public Task<AppPermissionStatus> RequestLocationAsync() =>
        Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(async () =>
            Map(await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<Microsoft.Maui.ApplicationModel.Permissions.LocationWhenInUse>()));

    /// <inheritdoc />
    public Task<bool> ShouldShowLocationRationaleAsync() =>
        Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() =>
            Microsoft.Maui.ApplicationModel.Permissions.ShouldShowRationale<Microsoft.Maui.ApplicationModel.Permissions.LocationWhenInUse>());

    private static AppPermissionStatus Map(Microsoft.Maui.ApplicationModel.PermissionStatus status) => status switch
    {
        Microsoft.Maui.ApplicationModel.PermissionStatus.Granted => AppPermissionStatus.Granted,
        Microsoft.Maui.ApplicationModel.PermissionStatus.Denied => AppPermissionStatus.Denied,
        Microsoft.Maui.ApplicationModel.PermissionStatus.Disabled => AppPermissionStatus.Disabled,
        Microsoft.Maui.ApplicationModel.PermissionStatus.Restricted => AppPermissionStatus.Restricted,
        Microsoft.Maui.ApplicationModel.PermissionStatus.Limited => AppPermissionStatus.Limited,
        _ => AppPermissionStatus.Unknown,
    };
}
