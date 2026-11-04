// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>
/// What the app currently knows about its own build, ready to show. The app's check service builds it from the server answer, the
/// store answer, the installed build, the dismissal and the UI language.
/// </summary>
/// <param name="Decision">What the app should do about its build.</param>
/// <param name="CurrentBuild">The installed build number, 0 when it could not be read.</param>
/// <param name="LatestBuild">
/// The build the banner announces (the store's, or the server's when the store cannot say); when there is none to offer, the
/// server's <c>LatestBuild</c>, or 0 with no server answer.
/// </param>
/// <param name="LatestVersion">
/// Display text for the newest build ("0.2.0"), or null when none is known. A store gives a build number, not a name, so it is set
/// only when the server names the very build that is announced.
/// </param>
/// <param name="StoreUrl">The server's absolute https store link, or null.</param>
/// <param name="Message">The server's message for the UI language, or null (never another language's text).</param>
/// <param name="Channel">This install's channel (see <see cref="InstallChannels"/>), used for wording; null when unknown.</param>
public sealed record AppUpdateState(
    AppUpdateDecision Decision,
    int CurrentBuild,
    int LatestBuild,
    string? LatestVersion,
    string? StoreUrl,
    string? Message,
    string? Channel = null)
{
    /// <summary>Nothing learned yet, or a check that has not succeeded. Shows nothing.</summary>
    public static AppUpdateState None { get; } = new(AppUpdateDecision.UpToDate, 0, 0, null, null, null);

    /// <summary>True when the build is installed from a download rather than a store, so the wording says "install", not "update from the store".</summary>
    public bool IsDirect => Channel == InstallChannels.Direct;

    /// <summary>True when the build is below the supported minimum.</summary>
    public bool IsRequired => Decision == AppUpdateDecision.UpdateRequired;

    /// <summary>True when a newer build is offered and the rider has not dismissed it.</summary>
    public bool IsAvailable => Decision == AppUpdateDecision.UpdateAvailable;
}
