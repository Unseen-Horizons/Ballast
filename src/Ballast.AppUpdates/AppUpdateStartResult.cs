// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>How an attempt to start an update ended. Only what the caller can act on.</summary>
public enum AppUpdateStartResult
{
    /// <summary>The store (or the platform's update flow) was opened. Whether the rider updates is up to them.</summary>
    Started,

    /// <summary>There is nothing to open: the server gave no store link and no platform flow exists.</summary>
    NoStoreLink,

    /// <summary>Opening it failed. The implementation logs it, and the app tells the user.</summary>
    Failed,
}
