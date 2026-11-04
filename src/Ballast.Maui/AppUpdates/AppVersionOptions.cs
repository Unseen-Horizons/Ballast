// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.AppUpdates;

/// <summary>
/// The keys and timing of the update check. The defaults are the app's own preference keys, so a build that reads them after an
/// upgrade finds what the earlier build wrote.
/// </summary>
public sealed record AppVersionOptions
{
    /// <summary>The preference that holds the time of the last successful check (UTC, round-trip format).</summary>
    public string LastCheckKey { get; init; } = "appversion.lastCheckUtc";

    /// <summary>The preference that holds the build the rider dismissed the offer for.</summary>
    public string DismissedKey { get; init; } = "appversion.dismissedLatestBuild";

    /// <summary>The preference that holds the last server answer, as JSON.</summary>
    public string LastResponseKey { get; init; } = "appversion.lastResponse";

    /// <summary>The preference that holds the last store answer, as JSON.</summary>
    public string LastStoreKey { get; init; } = "appversion.lastStore";

    /// <summary>The preference container, or null for the default one.</summary>
    public string? SharedPreferencesName { get; init; }

    /// <summary>How long the store may take to answer before the server's rule decides alone.</summary>
    public TimeSpan StoreTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The language the update message is picked in, as a two-letter code. Null for the current UI culture.</summary>
    public Func<string>? UiLanguage { get; init; }
}
