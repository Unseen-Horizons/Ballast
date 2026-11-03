// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Threading;

using Android.Views;

using Ballast.Maui.Diagnostics;
using Ballast.Maui.Platform;

using Microsoft.Maui.ApplicationModel;

namespace Ballast.Maui.Adapters;

/// <summary>
/// Android's install channel, from the installer package the package manager records. Google Play is <see cref="InstallChannels.Play"/>; anything
/// else, and no installer at all, is <see cref="InstallChannels.Direct"/>. If the package manager cannot be read the channel is unknown (null), and
/// the cause is traced: guessing "direct" would tell a Play install to fetch an APK, and guessing "play" the reverse.
/// </summary>
public sealed class AndroidInstallChannel : IInstallChannel
{
    /// <inheritdoc />
    public string? Channel
    {
        get
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var packageName = context.PackageName;
                if (packageName is null || context.PackageManager is not { } packages)
                {
                    Trace.WriteLine("[InstallChannel] No package manager or package name; the install source is unknown.");
                    return null;
                }

                string? installer;
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    installer = packages.GetInstallSourceInfo(packageName).InstallingPackageName;
                }
                else
                {
#pragma warning disable CA1422 // Superseded by GetInstallSourceInfo on API 30+, which the branch above uses.
                    installer = packages.GetInstallerPackageName(packageName);
#pragma warning restore CA1422
                }

                return InstallChannels.FromAndroidInstaller(installer);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[InstallChannel] Could not read the install source ({ex.GetType().Name}: {ex.Message}); the channel is unknown.");
                return null;
            }
        }
    }
}

/// <summary>
/// Full brightness for the app's window while a code is shown full screen. It sets the window's own brightness override (the value a video player
/// sets), never the system setting, so it cannot outlive the app and needs no permission.
/// </summary>
public sealed class AndroidScreenBrightness : IScreenBrightness
{
    /// <summary>What the window had before the first <see cref="SetFull"/> of a raise. Null while not raised.</summary>
    private float? _before;

    /// <inheritdoc />
    public void SetFull() => MainThread.BeginInvokeOnMainThread(() =>
    {
        try
        {
            var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
            if (window?.Attributes is not { } attributes)
                return;

            // Kept from the first call only: a second SetFull would otherwise "remember" full brightness.
            _before ??= attributes.ScreenBrightness;
            attributes.ScreenBrightness = 1f;
            window.Attributes = attributes;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ScreenBrightness] Could not raise the brightness: {ex.Message}");
        }
    });

    /// <inheritdoc />
    public void Restore() => MainThread.BeginInvokeOnMainThread(() =>
    {
        try
        {
            var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
            if (window?.Attributes is not { } attributes)
                return;

            attributes.ScreenBrightness = _before ?? WindowManagerLayoutParams.BrightnessOverrideNone;
            window.Attributes = attributes;
            _before = null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ScreenBrightness] Could not restore the brightness: {ex.Message}");
        }
    });
}

/// <summary>
/// The notification permission Android 13 and later asks for before the app can post a notification. Earlier versions grant it at install, so the
/// answer there is whether the notifications are enabled.
/// </summary>
public static class NotificationPermission
{
    /// <summary>
    /// Asks for the notification permission when it is not granted, and says whether it is granted now. Runs on the UI thread.
    /// </summary>
    /// <returns>True when notifications may be posted after the ask.</returns>
    public static async Task<bool> RequestAsync()
    {
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
        {
            var status = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var current = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<Microsoft.Maui.ApplicationModel.Permissions.PostNotifications>();
                return current == PermissionStatus.Granted
                    ? current
                    : await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<Microsoft.Maui.ApplicationModel.Permissions.PostNotifications>();
            });
            return status == PermissionStatus.Granted;
        }

        return global::AndroidX.Core.App.NotificationManagerCompat.From(global::Android.App.Application.Context)!.AreNotificationsEnabled();
    }
}

/// <summary>
/// Sends the Android runtime's unhandled exceptions to a crash reporter. The runtime still ends the process; this only gets the report on disk
/// first. Attach once.
/// </summary>
public static class AndroidCrashHooks
{
    private static int _attached;

    /// <summary>Attaches the runtime's hook to <paramref name="reporter"/>. A second call does nothing.</summary>
    /// <param name="reporter">The reporter that writes the report.</param>
    public static void Attach(CrashReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        if (Interlocked.Exchange(ref _attached, 1) == 1)
            return;

        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => reporter.Record(e.Exception, "Android runtime");
    }
}
