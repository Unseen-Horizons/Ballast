// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>What the app should do about its own build, given what the server says is published.</summary>
public enum AppUpdateDecision
{
    /// <summary>Nothing to show: the build is current (or newer, as a development build is), or nothing has been learned.</summary>
    UpToDate,

    /// <summary>A newer build exists. Offered on Home, and the rider may dismiss it.</summary>
    UpdateAvailable,

    /// <summary>A newer build exists but the rider already dismissed this one. Comes back only for a still newer build.</summary>
    UpdateAvailableDismissed,

    /// <summary>
    /// This build is below the supported minimum. Announced on Home with an Update button and no way to dismiss it; the app keeps
    /// working, a build is never locked out.
    /// </summary>
    UpdateRequired,
}
