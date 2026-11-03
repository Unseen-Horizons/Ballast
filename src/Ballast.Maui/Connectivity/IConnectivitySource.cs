// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Connectivity;

/// <summary>
/// Where the app hears about the network: whether the device has a route to the internet now, whether it is on a cellular
/// connection, and each change. On a phone the production source reads the platform's connectivity API; a test hands in a fake.
/// </summary>
public interface IConnectivitySource
{
    /// <summary>Raised with the new offline value whenever connectivity changes. Raised on a platform thread.</summary>
    event EventHandler<bool>? Changed;

    /// <summary>Whether the device currently reports no route to the internet.</summary>
    /// <returns>True when offline.</returns>
    bool IsOfflineNow();

    /// <summary>Whether the device's current connection is cellular (mobile data), which some downloads ask before using.</summary>
    /// <returns>True when a cellular connection is in use.</returns>
    bool IsOnCellular();
}
