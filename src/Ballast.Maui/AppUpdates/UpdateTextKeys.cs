// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.AppUpdates;

/// <summary>
/// The localisation keys of the update prompts. The defaults are the keys the app's strings use; another app passes its own. The library
/// names no words of its own.
/// </summary>
public sealed record UpdateTextKeys
{
    /// <summary>The title of the "update downloaded" prompt.</summary>
    public string DownloadedTitle { get; init; } = "Update_DownloadedTitle";

    /// <summary>The body of the "update downloaded" prompt.</summary>
    public string DownloadedBody { get; init; } = "Update_DownloadedBody";

    /// <summary>The button that restarts now.</summary>
    public string Restart { get; init; } = "Update_Restart";

    /// <summary>The button that restarts later.</summary>
    public string Later { get; init; } = "Update_Later";

    /// <summary>The title shown when a direct download did not open.</summary>
    public string DownloadFailedTitle { get; init; } = "Update_DownloadFailedTitle";

    /// <summary>The body shown when a direct download did not open.</summary>
    public string DownloadFailedBody { get; init; } = "Update_DownloadFailedBody";

    /// <summary>The title shown when the store did not open.</summary>
    public string StoreFailedTitle { get; init; } = "Update_StoreFailedTitle";

    /// <summary>The body shown when the store did not open.</summary>
    public string StoreFailedBody { get; init; } = "Update_StoreFailedBody";

    /// <summary>The button that closes an alert.</summary>
    public string Ok { get; init; } = "App_Ok";
}
