// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Storage;

/// <summary>
/// The two folders an app writes to on a phone: its own data folder (kept, and included in backups unless a rule excludes it)
/// and its cache folder (which the system may clear). A library never writes anywhere else.
/// </summary>
public interface IAppPaths
{
    /// <summary>The app's private data folder. Files written here are the app's own.</summary>
    string AppDataDirectory { get; }

    /// <summary>The app's cache folder. The system may empty it at any time.</summary>
    string CacheDirectory { get; }
}
