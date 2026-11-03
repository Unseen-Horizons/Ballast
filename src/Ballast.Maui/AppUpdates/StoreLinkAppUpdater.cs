// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

using Ballast.AppUpdates;
using Ballast.Maui.Platform;

namespace Ballast.Maui.AppUpdates;

/// <summary>
/// Opens the store's listing for the update. Only an https address is opened. When the platform will not open it, the rider is told
/// which page failed, and the start reports <see cref="AppUpdateStartResult.Failed"/>.
/// </summary>
public sealed class StoreLinkAppUpdater : IAppUpdater
{
    private readonly IStoreLinkLauncher _launcher;
    private readonly IUpdatePrompts _prompts;
    private readonly IInstallChannel? _channel;

    /// <summary>Creates the updater.</summary>
    /// <param name="launcher">Opens an address on the device.</param>
    /// <param name="prompts">Tells the rider when the page did not open.</param>
    /// <param name="channel">Where this install came from. A direct install is told about its download, not a store.</param>
    public StoreLinkAppUpdater(IStoreLinkLauncher launcher, IUpdatePrompts prompts, IInstallChannel? channel = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(prompts);
        _launcher = launcher;
        _prompts = prompts;
        _channel = channel;
    }

    /// <inheritdoc />
    public bool CanStart(string? storeUrl) => TryGetStoreUri(storeUrl, out _);

    /// <inheritdoc />
    public async Task<AppUpdateStartResult> StartAsync(string? storeUrl)
    {
        if (!TryGetStoreUri(storeUrl, out var uri))
        {
            Trace.WriteLine("[AppUpdater] No store link to open.");
            return AppUpdateStartResult.NoStoreLink;
        }

        try
        {
            if (await _launcher.OpenAsync(uri).ConfigureAwait(false))
                return AppUpdateStartResult.Started;

            Trace.WriteLine($"[AppUpdater] The platform would not open {uri.Host}.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppUpdater] Opening the store link failed: {ex.Message}");
        }

        await _prompts.ShowStoreNotOpenedAsync(_channel?.Channel == InstallChannels.Direct).ConfigureAwait(false);
        return AppUpdateStartResult.Failed;
    }

    private static bool TryGetStoreUri(string? storeUrl, out Uri uri)
    {
        if (Uri.TryCreate(storeUrl, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}

/// <summary>Opens an address on the device (the platform's browser or store).</summary>
public interface IStoreLinkLauncher
{
    /// <summary>Opens the address. True when the platform took it.</summary>
    Task<bool> OpenAsync(Uri uri);
}

/// <summary>The rider-facing prompts of the update flow. The app's own wording is behind them.</summary>
public interface IUpdatePrompts
{
    /// <summary>Asks whether to restart now to finish a downloaded update. True when the rider chose to restart.</summary>
    Task<bool> AskToRestartAsync();

    /// <summary>Tells the rider the store or the download page did not open. <paramref name="direct"/> is true for a direct download.</summary>
    Task ShowStoreNotOpenedAsync(bool direct);
}
