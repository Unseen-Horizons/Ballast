// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>
/// What the platform's own store says about an update (Google Play on Android), as <see cref="IAppUpdater.IsUpdateAvailableAsync"/>
/// reports it. <paramref name="VersionCode"/> is the store's newest build number (Play's <c>availableVersionCode</c>), the same
/// number space as the installed build number; 0 when there is no update.
/// </summary>
/// <param name="Available">True when the store has a newer build than the installed one.</param>
/// <param name="VersionCode">The store's newest build number, or 0 when there is no update.</param>
public sealed record StoreUpdate(bool Available, int VersionCode);
