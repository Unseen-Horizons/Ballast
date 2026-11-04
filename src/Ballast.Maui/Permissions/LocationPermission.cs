// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;
using Ballast.Maui.Storage;

namespace Ballast.Maui.Permissions;

/// <summary>The platform families the permission rules differ by.</summary>
public enum PermissionPlatform
{
    /// <summary>Android.</summary>
    Android,

    /// <summary>iOS.</summary>
    Ios,

    /// <summary>Windows.</summary>
    Windows,

    /// <summary>Any other platform.</summary>
    Other,
}

/// <summary>The status a platform reports for a permission.</summary>
public enum AppPermissionStatus
{
    /// <summary>The platform did not say.</summary>
    Unknown,

    /// <summary>Refused.</summary>
    Denied,

    /// <summary>Switched off for the device.</summary>
    Disabled,

    /// <summary>Restricted by policy.</summary>
    Restricted,

    /// <summary>Granted.</summary>
    Granted,

    /// <summary>Granted for a limited scope.</summary>
    Limited,
}

/// <summary>
/// The platform's permission calls, as the library needs them. The MAUI implementation dispatches each call to the UI thread.
/// </summary>
public interface IPermissionSource
{
    /// <summary>Reads whether location while in use is allowed, without asking.</summary>
    /// <returns>The platform's status.</returns>
    Task<AppPermissionStatus> CheckLocationAsync();

    /// <summary>Asks for location while in use, and returns the status the platform then reports.</summary>
    /// <returns>The status after the prompt.</returns>
    Task<AppPermissionStatus> RequestLocationAsync();

    /// <summary>Whether the platform says the permission was refused before and should be explained before it is asked again.</summary>
    /// <returns>True after one refusal; false before any request, after a second refusal, or on a platform that does not say.</returns>
    Task<bool> ShouldShowLocationRationaleAsync();
}

/// <summary>
/// The app's location permission in one place: the first-start flow, the Start and Remind me asks, and the map's silent ask all read "is location
/// allowed" the same way, so a platform quirk is handled once.
/// </summary>
public sealed class LocationPermission
{
    /// <summary>
    /// The install-state key for "this install has shown the location system prompt". It is in the per-install file, so a reinstall or a new phone
    /// reads it as not asked, which is what the platform's own memory says too.
    /// </summary>
    public const string LocationRequestedKey = "permissions.locationRequested";

    private readonly IPermissionSource _source;
    private readonly InstallAnswers _answers;
    private readonly PermissionPlatform _platform;
    private readonly IClientLog _log;

    /// <summary>Creates the permission view.</summary>
    /// <param name="source">The platform's permission calls.</param>
    /// <param name="answers">Where the "shown the prompt" answer is kept.</param>
    /// <param name="platform">The platform, which decides how "unknown" reads.</param>
    /// <param name="log">Where a failed check is logged.</param>
    public LocationPermission(IPermissionSource source, InstallAnswers answers, PermissionPlatform platform, IClientLog log)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(log);
        _source = source;
        _answers = answers;
        _platform = platform;
        _log = log;
    }

    /// <summary>
    /// Whether this install has shown the location system prompt. A restored install reads false: its grant did not come back, so the prompt is the
    /// first question again.
    /// </summary>
    public bool LocationRequestedOnThisInstall => _answers.Get(LocationRequestedKey, missing: false, unreadable: false);

    /// <summary>
    /// Whether a status counts as allowed. Windows reports "unknown" when there is no grant to ask for, which is not a refusal; on iOS "unknown" means
    /// not asked yet, so it is not allowed.
    /// </summary>
    /// <param name="status">The platform's status.</param>
    /// <param name="platform">The platform.</param>
    /// <returns>True when the app may use location.</returns>
    public static bool IsAllowed(AppPermissionStatus status, PermissionPlatform platform) =>
        status == AppPermissionStatus.Granted || (status == AppPermissionStatus.Unknown && platform == PermissionPlatform.Windows);

    /// <summary>
    /// Whether location while in use is allowed, without asking. A check that throws is logged and read as allowed: there is then nothing the app
    /// could ask.
    /// </summary>
    /// <returns>True when allowed, or when the check failed.</returns>
    public async Task<bool> IsLocationAllowedAsync()
    {
        try
        {
            return IsAllowed(await _source.CheckLocationAsync(), _platform);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LocationPermission), $"location status unavailable ({ex.GetType().Name})");
            return true;
        }
    }

    /// <summary>
    /// Asks for location while in use if it is not allowed yet, and says whether it is now. The system prompt may not appear (a permanent refusal
    /// answers at once); the caller decides what to tell the person. Records that the prompt was shown just before the request, so
    /// <see cref="LocationRequestedOnThisInstall"/> is true even if the app is killed while the prompt is up.
    /// </summary>
    /// <returns>True when allowed after the request; false when the request failed (logged).</returns>
    public async Task<bool> RequestLocationAsync()
    {
        try
        {
            var status = await _source.CheckLocationAsync();
            if (!IsAllowed(status, _platform))
            {
                _answers.Set(LocationRequestedKey, true);
                status = await _source.RequestLocationAsync();
            }

            return IsAllowed(status, _platform);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LocationPermission), $"location request failed ({ex.GetType().Name})");
            return false;
        }
    }

    /// <summary>
    /// Whether the platform says the permission should be explained before it is asked again. A check that throws is logged and read as false, which
    /// leaves the decision to <see cref="LocationRequestedOnThisInstall"/>.
    /// </summary>
    /// <returns>True when a rationale should be shown first.</returns>
    public async Task<bool> ShouldShowLocationRationaleAsync()
    {
        try
        {
            return await _source.ShouldShowLocationRationaleAsync();
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LocationPermission), $"location rationale check failed ({ex.GetType().Name})");
            return false;
        }
    }
}
