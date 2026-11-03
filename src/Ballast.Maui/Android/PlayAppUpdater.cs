// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

using Android.Gms.Extensions;

using Ballast.AppUpdates;
using Ballast.Maui.Platform;

using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;

using PlayUpdateOptions = Xamarin.Google.Android.Play.Core.AppUpdate.AppUpdateOptions;

namespace Ballast.Maui.AppUpdates;

/// <summary>
/// Android's update flow: Google Play In-App Updates when the app was installed from Play (always the flexible, background flow: no
/// blocking, full-screen update is ever started), the store link otherwise. Falling back to the store link is not a substitution when Play
/// cannot update the app anyway (a sideloaded build), or when Play says there is no update while the server says there is (a staged
/// rollout, Play's cache). Every other fallback follows a failed Play call. Each fallback logs which path ran and why.
/// </summary>
public sealed class PlayAppUpdater : IAppUpdater, IDisposable
{
    private readonly IInstallChannel _channel;
    private readonly StoreLinkAppUpdater _storeLink;
    private readonly IUpdatePrompts _prompts;
    private readonly object _gate = new();
    private IAppUpdateManager? _flexibleManager;
    private InstallListener? _flexibleListener;

    /// <summary>Creates the updater.</summary>
    /// <param name="channel">Where this install came from. Play is used only for a Play install.</param>
    /// <param name="launcher">Opens the store link when Play cannot be used.</param>
    /// <param name="prompts">The restart prompt and the failure notice.</param>
    public PlayAppUpdater(IInstallChannel channel, IStoreLinkLauncher launcher, IUpdatePrompts prompts)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(prompts);
        _channel = channel;
        _prompts = prompts;
        _storeLink = new StoreLinkAppUpdater(launcher, prompts, channel);
    }

    /// <summary>A Play call that handed back no task is a failure like any other: it ends in the store-link fallback.</summary>
    private static Task<T> Await<T>(global::Android.Gms.Tasks.Task? task) where T : class, global::Android.Runtime.IJavaObject =>
        (task ?? throw new InvalidOperationException("Play returned no task.")).AsAsync<T>();

    /// <inheritdoc />
    public void Dispose() => UnregisterFlexibleListener();

    /// <inheritdoc />
    public bool CanStart(string? storeUrl) => IsInstalledFromPlay() || _storeLink.CanStart(storeUrl);

    /// <inheritdoc />
    public async Task<AppUpdateStartResult> StartAsync(string? storeUrl)
    {
        if (!IsInstalledFromPlay())
        {
            Trace.WriteLine("[PlayUpdate] Not installed from Google Play (Play cannot update a sideloaded build): opening the store link.");
            return await _storeLink.StartAsync(storeUrl).ConfigureAwait(false);
        }

        try
        {
            if (await StartPlayFlowAsync().ConfigureAwait(false) is { } started)
                return started;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PlayUpdate] The Play update flow failed ({ex.GetType().Name}: {ex.Message}): opening the store link.");
        }

        // Play failed, or has nothing to start for this build although the server says there is an update.
        return await _storeLink.StartAsync(storeUrl).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks Play itself whether a newer build exists, so the banner needs no server value. True for <c>UpdateAvailable</c> and for an update
    /// already in progress, with Play's available version code (what a dismissal is remembered against). False for <c>UpdateNotAvailable</c>.
    /// Null (cannot tell, so the server's build number decides) when the app was not installed from Play, when Play's availability is
    /// unknown, or when the call fails: each is logged with <c>[PlayUpdate]</c>. Never throws.
    /// </summary>
    public async Task<StoreUpdate?> IsUpdateAvailableAsync()
    {
        if (!IsInstalledFromPlay())
        {
            Trace.WriteLine("[PlayUpdate] Not installed from Google Play: Play cannot say whether an update exists, so the server's build number decides.");
            return null;
        }

        try
        {
            // The application context, not an activity: the check also runs at start-up, before one is in front.
            var manager = AppUpdateManagerFactory.Create(global::Android.App.Application.Context);
            var info = await Await<AppUpdateInfo>(manager.GetAppUpdateInfo()).ConfigureAwait(false);
            var availability = info.UpdateAvailability();
            if (availability == UpdateAvailability.UpdateAvailable || availability == UpdateAvailability.DeveloperTriggeredUpdateInProgress)
            {
                var code = info.AvailableVersionCode();
                Trace.WriteLine($"[PlayUpdate] Play reports an update (availability {availability}, version code {code}).");
                return new StoreUpdate(true, code);
            }

            if (availability == UpdateAvailability.UpdateNotAvailable)
                return new StoreUpdate(false, 0);

            Trace.WriteLine($"[PlayUpdate] Play's update availability is {availability}: it cannot say, so the server's build number decides.");
            return null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PlayUpdate] Asking Play about an update failed ({ex.GetType().Name}: {ex.Message}): the server's build number decides.");
            return null;
        }
    }

    /// <summary>
    /// Starts Play's flexible (background) update, never an immediate, full-screen one, so an update can never take the app over. Started
    /// when Play took the request (or the rider declined it); null when Play has nothing to start.
    /// </summary>
    private async Task<AppUpdateStartResult?> StartPlayFlowAsync()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            ?? throw new InvalidOperationException("There is no current activity to show the Play update over.");
        var manager = AppUpdateManagerFactory.Create(activity);
        var info = await Await<AppUpdateInfo>(manager.GetAppUpdateInfo()).ConfigureAwait(false);
        const string kind = "flexible";
        var availability = info.UpdateAvailability();

        // An update Play already has in progress is continued, not restarted from scratch.
        var resuming = availability == UpdateAvailability.DeveloperTriggeredUpdateInProgress;
        if (availability != UpdateAvailability.UpdateAvailable && !resuming)
        {
            Trace.WriteLine($"[PlayUpdate] Play reports no update (availability {availability}) while the server reports one: opening the store link.");
            return null;
        }

        var options = PlayUpdateOptions.DefaultOptions(AppUpdateType.Flexible);
        if (!resuming && !info.IsUpdateTypeAllowed(options))
        {
            Trace.WriteLine($"[PlayUpdate] Play does not allow a {kind} update right now: opening the store link.");
            return null;
        }

        RegisterFlexibleListener(manager);
        var code = (await Await<Java.Lang.Integer>(manager.StartUpdateFlow(info, activity, options)).ConfigureAwait(false)).IntValue();
        if (code == (int)global::Android.App.Result.Ok)
        {
            Trace.WriteLine($"[PlayUpdate] The {kind} update flow ran.");
            return AppUpdateStartResult.Started;
        }

        UnregisterFlexibleListener();
        if (code == (int)global::Android.App.Result.Canceled)
        {
            // The rider saw Play's prompt and said no. Not a failure, and nothing to fall back to.
            Trace.WriteLine($"[PlayUpdate] The rider declined the {kind} Play update.");
            return AppUpdateStartResult.Started;
        }

        throw new InvalidOperationException($"Play's update flow ended with result code {code}.");
    }

    private void RegisterFlexibleListener(IAppUpdateManager manager)
    {
        lock (_gate)
        {
            // One listener at a time: a second tap on Update must not stack a second restart dialog.
            if (_flexibleListener is not null && _flexibleManager is not null)
                _flexibleManager.UnregisterListener(_flexibleListener);

            _flexibleManager = manager;
            _flexibleListener = new InstallListener(this);
            manager.RegisterListener(_flexibleListener);
        }
    }

    private IAppUpdateManager? UnregisterFlexibleListener()
    {
        lock (_gate)
        {
            var manager = _flexibleManager;
            if (manager is not null && _flexibleListener is not null)
                manager.UnregisterListener(_flexibleListener);

            _flexibleManager = null;
            _flexibleListener = null;
            return manager;
        }
    }

    private void OnInstallStatus(int status)
    {
        if (status == InstallStatus.Downloaded)
        {
            // Offered once: the listener goes before the prompt, so a later state change cannot ask again.
            _ = AskToRestartAsync(UnregisterFlexibleListener());
        }
        else if (status is InstallStatus.Failed or InstallStatus.Canceled)
        {
            Trace.WriteLine($"[PlayUpdate] The flexible update ended without installing (status {status}).");
            UnregisterFlexibleListener();
        }
    }

    private async Task AskToRestartAsync(IAppUpdateManager? manager)
    {
        try
        {
            if (manager is null)
            {
                Trace.WriteLine("[PlayUpdate] The update is downloaded, but there is no update manager to finish it with.");
                return;
            }

            if (!await _prompts.AskToRestartAsync().ConfigureAwait(false))
                return;

            await Await<Java.Lang.Void>(manager.CompleteUpdate()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PlayUpdate] Restarting to finish the update failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// True when the install channel is Play. An unknown channel (the install source could not be read, already logged by the channel)
    /// counts as not Play, so the store-link path runs.
    /// </summary>
    private bool IsInstalledFromPlay() => _channel.Channel == InstallChannels.Play;

    private sealed class InstallListener : Java.Lang.Object, IInstallStateUpdatedListener
    {
        private readonly PlayAppUpdater _owner;

        public InstallListener(PlayAppUpdater owner) => _owner = owner;

        public void OnStateUpdate(InstallState? state)
        {
            try
            {
                if (state is not null)
                    _owner.OnInstallStatus(state.InstallStatus());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PlayUpdate] Install state handling failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
