// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>What the banner on the app's home screen shows for an <see cref="AppUpdateDecision"/>.</summary>
public enum UpdateBannerKind
{
    /// <summary>No banner.</summary>
    None,

    /// <summary>"Update available": the rider may dismiss it until a still newer build is published.</summary>
    Offer,

    /// <summary>"No longer supported": always shown while the build is below the minimum, and cannot be dismissed.</summary>
    Required,
}
