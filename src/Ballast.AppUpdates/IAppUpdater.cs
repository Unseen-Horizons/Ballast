// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>
/// Starts the update. That is the store listing, or a platform implementation (Play in-app updates on Android) where one
/// exists. An update never takes the app over: the app is only ever told that one exists, so there is no blocking (immediate)
/// flow to start. The platform implements it; this library holds the contract and the rules that use it.
/// </summary>
public interface IAppUpdater
{
    /// <summary>
    /// True when <see cref="StartAsync"/> has something to open for this link. The banner shows its Update button only when this
    /// is true, so a button never does nothing.
    /// </summary>
    /// <param name="storeUrl">The server's absolute https link, or null.</param>
    /// <returns>True when there is something to open.</returns>
    bool CanStart(string? storeUrl);

    /// <summary>
    /// Starts the update, in the background where the platform can (Play's flexible flow).
    /// </summary>
    /// <param name="storeUrl">The server's absolute https link, or null.</param>
    /// <returns>How the attempt ended. See <see cref="AppUpdateStartResult"/>.</returns>
    Task<AppUpdateStartResult> StartAsync(string? storeUrl);

    /// <summary>
    /// Whether the platform's own store has a newer build than the one installed (Google Play on Android), with the store's build
    /// number so a dismissal can be remembered per version. Null means this updater cannot tell (no store flow, a sideloaded
    /// install, or a failed call, which the implementation logs); the server's <c>LatestBuild</c> then decides. It never throws.
    /// The default answers null.
    /// </summary>
    /// <returns>The store's answer, or null when it cannot tell.</returns>
    Task<StoreUpdate?> IsUpdateAvailableAsync() => Task.FromResult<StoreUpdate?>(null);
}
