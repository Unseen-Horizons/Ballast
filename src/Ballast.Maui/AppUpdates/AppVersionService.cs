// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Ballast.AppUpdates;
using Ballast.Maui.Platform;
using Ballast.Maui.Storage;

using Microsoft.Extensions.Logging;

namespace Ballast.Maui.AppUpdates;

/// <summary>
/// The device's update check: asks the server for the build numbers, asks the store what it has, and keeps the last answers so a
/// start that is not due a check shows the same banner. Every question carries the install channel, because a build is announced
/// only once it is installable through the channel that carries it.
/// </summary>
/// <remarks>
/// The decisions are <see cref="AppVersionRules"/> (the server's rules, the store's rules, when a check is due). This class only
/// reads the device, runs the call and keeps the answers. Its preference keys are the app's, byte for byte: a value stored by an
/// earlier build is read by this one.
/// </remarks>
public sealed class AppVersionService : IDisposable
{
    private readonly AppVersionClient _client;
    private readonly IPreferenceStore _preferences;
    private readonly IInstallChannel _channel;
    private readonly IUpdateDevice _device;
    private readonly IAppUpdater? _updater;
    private readonly AppVersionOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private AppVersionResponse? _info;
    private StoreUpdate? _store;
    private int _currentBuild;

    /// <summary>Creates the service.</summary>
    /// <param name="client">The client for the server's version answer.</param>
    /// <param name="preferences">The device's preferences, where the answers and the dismissal are kept.</param>
    /// <param name="channel">Where this install came from.</param>
    /// <param name="device">The device facts the check needs: the stamped build, the platform's name, the manifest's build text.</param>
    /// <param name="updater">The store's update flow, or null when the store cannot say.</param>
    /// <param name="options">The keys and the store timeout, or null for the app's defaults.</param>
    /// <param name="clock">The clock for the check interval. The system clock when null.</param>
    /// <param name="logger">Where a store answer that cannot be used is logged (no answer, a failed call, no build number). Null, the default, logs nothing there.</param>
    public AppVersionService(
        AppVersionClient client,
        IPreferenceStore preferences,
        IInstallChannel channel,
        IUpdateDevice device,
        IAppUpdater? updater = null,
        AppVersionOptions? options = null,
        TimeProvider? clock = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(device);
        _client = client;
        _preferences = preferences;
        _channel = channel;
        _device = device;
        _updater = updater;
        _options = options ?? new AppVersionOptions();
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>Raised when the state changes.</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>The update state as it stands now: the last answers, and the dismissal.</summary>
    public AppUpdateState Current
    {
        get
        {
            AppVersionResponse? info;
            StoreUpdate? store;
            int build;
            lock (_sync)
            {
                info = _info;
                store = _store;
                build = _currentBuild;
            }

            if ((info is null && store is null) || build <= 0)
                return AppUpdateState.None;

            var decision = AppVersionRules.Decide(build, info, store, ReadDismissed());
            var announced = AppVersionRules.AnnouncedBuild(build, info, store);
            var latest = announced > 0 ? announced : info?.LatestBuild ?? 0;
            var language = _options.UiLanguage?.Invoke() ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return new AppUpdateState(
                decision,
                build,
                latest,
                info is not null && info.LatestBuild == latest ? info.LatestVersion : null,
                info?.StoreUrl,
                AppVersionRules.PickMessage(info?.Messages, language),
                _channel.Channel);
        }
    }

    /// <summary>
    /// Subscribes without keeping the subscriber alive: once it is collected the handler unsubscribes itself, so a page that goes
    /// away never leaves a dead callback behind.
    /// </summary>
    /// <typeparam name="T">The subscriber's type.</typeparam>
    /// <param name="subscriber">The subscriber.</param>
    /// <param name="onChanged">Runs with the subscriber when the state changes.</param>
    public void SubscribeWeak<T>(T subscriber, Action<T> onChanged) where T : class
    {
        var weak = new WeakReference<T>(subscriber);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (weak.TryGetTarget(out var target))
                onChanged(target);
            else
                Changed -= handler;
        };
        Changed += handler;
    }

    /// <summary>
    /// Checks for an update when one is due (or when <paramref name="force"/> is set), and returns the state. A failed check is logged and
    /// leaves the last answers in place. It never throws.
    /// </summary>
    /// <param name="force">Checks now, whatever the interval says.</param>
    /// <param name="ct">Cancels the check.</param>
    public async Task<AppUpdateState> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!TryReadBuild(out var build))
                return Current;

            if (_device.PlatformQueryName is not { } platform)
            {
                Trace.WriteLine($"[AppVersion] No update check on {_device.PlatformName ?? "this platform"}.");
                return Current;
            }

            if (_channel.Channel is not { } channel)
            {
                // Already logged by the channel. A guess would announce another channel's build, or say "no longer supported" on its minimum.
                Trace.WriteLine("[AppVersion] The install channel is unknown; no update check.");
                return Current;
            }

            lock (_sync)
                _currentBuild = build;

            var stored = ReadStoredResponse();
            var storedStore = ReadStoredStore();
            if (!force)
            {
                var wasRequired = AppVersionRules.Decide(build, stored, storedStore, ReadDismissed()) == AppUpdateDecision.UpdateRequired;
                if (!AppVersionRules.ShouldCheck(ReadLastCheck(), _clock.GetUtcNow().UtcDateTime, wasRequired))
                {
                    Apply(stored, replaceServer: true, storedStore);
                    return Current;
                }
            }

            var answer = await _client.GetAsync(platform, channel, ct).ConfigureAwait(false);
            var store = await AppVersionRules.AskStoreAsync(AppVersionRules.StoreUpdaterFor(channel, _updater), _options.StoreTimeout, _logger, ct).ConfigureAwait(false);
            SaveStore(store);
            if (answer.Answered)
            {
                // A 404 is an answer too: "no minimum", so the last server answer is dropped rather than kept.
                SaveCheck(answer.Info);
                Apply(answer.Info, replaceServer: true, store);
            }
            else
            {
                // The server could not be reached: it keeps what it said before, and the check is not recorded, so the next start asks
                // again. The store's answer is fresh and is used.
                Apply(null, replaceServer: false, store);
            }

            return Current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.WriteLine($"[AppVersion] The update check failed: {ex.Message}");
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Hides the offer for the build it names. A required update cannot be dismissed, so it stays.</summary>
    public void DismissBanner()
    {
        var state = Current;
        if (!state.IsAvailable || state.LatestBuild <= 0)
            return;

        try
        {
            _preferences.Set(_options.DismissedKey, state.LatestBuild, _options.SharedPreferencesName);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppVersion] Could not save the dismissal: {ex.Message}");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(AppVersionResponse? info, bool replaceServer, StoreUpdate? store)
    {
        lock (_sync)
        {
            if (replaceServer)
                _info = info;
            _store = store;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsUsable(AppVersionResponse? info) => info is { LatestBuild: > 0 } or { MinimumBuild: > 0 };

    /// <summary>
    /// The installed build: the stamp in the assembly. Where the platform's manifest carries the build number, the stamp must agree
    /// with it: a difference means the stamp and the manifest drifted apart, and the check would compare the wrong number.
    /// </summary>
    private bool TryReadBuild(out int build)
    {
        build = 0;
        try
        {
            if (_device.StampedBuild is not { } stamped)
            {
                Trace.WriteLine("[AppVersion] The build number is not stamped into the assembly; no update check.");
                return false;
            }

            if (_device.ManifestBuildText is { } manifest && (!AppBuildStamp.TryParse(manifest, out var manifestBuild) || manifestBuild != stamped))
            {
                Trace.WriteLine($"[AppVersion] The stamped build {stamped} differs from the platform's build number '{manifest}'; no update check.");
                return false;
            }

            build = stamped;
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppVersion] Could not read the build number: {ex.Message}");
        }

        build = 0;
        return false;
    }

    private int? ReadDismissed()
    {
        try
        {
            var value = _preferences.Get(_options.DismissedKey, 0, _options.SharedPreferencesName);
            return value > 0 ? value : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppVersion] Could not read the dismissal: {ex.Message}");
            return null;
        }
    }

    private DateTime? ReadLastCheck()
    {
        try
        {
            var text = _preferences.Get(_options.LastCheckKey, string.Empty, _options.SharedPreferencesName);
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at.ToUniversalTime()
                : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppVersion] Could not read the last check time: {ex.Message}");
            return null;
        }
    }

    private AppVersionResponse? ReadStoredResponse()
    {
        try
        {
            var json = _preferences.Get(_options.LastResponseKey, string.Empty, _options.SharedPreferencesName);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var info = JsonSerializer.Deserialize(json, AppVersionStoreJson.Default.AppVersionResponse);
            return IsUsable(info) ? info : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppVersion] Could not read the stored answer: {ex.Message}");
            return null;
        }
    }

    private StoreUpdate? ReadStoredStore()
    {
        try
        {
            var json = _preferences.Get(_options.LastStoreKey, string.Empty, _options.SharedPreferencesName);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, AppVersionStoreJson.Default.StoreUpdate);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppVersion] Could not read the stored store answer: {ex.Message}");
            return null;
        }
    }

    private void SaveCheck(AppVersionResponse? info)
    {
        try
        {
            if (info is null)
                _preferences.Remove(_options.LastResponseKey, _options.SharedPreferencesName);
            else
                _preferences.Set(_options.LastResponseKey, JsonSerializer.Serialize(info, AppVersionStoreJson.Default.AppVersionResponse), _options.SharedPreferencesName);

            _preferences.Set(_options.LastCheckKey, _clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture), _options.SharedPreferencesName);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppVersion] Could not save the check: {ex.Message}");
        }
    }

    private void SaveStore(StoreUpdate? store)
    {
        try
        {
            if (store is null)
                _preferences.Remove(_options.LastStoreKey, _options.SharedPreferencesName);
            else
                _preferences.Set(_options.LastStoreKey, JsonSerializer.Serialize(store, AppVersionStoreJson.Default.StoreUpdate), _options.SharedPreferencesName);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppVersion] Could not save the store answer: {ex.Message}");
        }
    }
}
