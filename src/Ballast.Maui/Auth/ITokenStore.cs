// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Auth;

/// <summary>
/// Where the tokens are kept on the device. The app implements it over its own secure store and cache: the library never
/// reads or writes a token itself. A read is expected to fail soft (an unreadable token reads as absent), so the rider signs
/// in again rather than the app crashing.
/// </summary>
public interface ITokenStore
{
    /// <summary>The stored access token, or null when there is none.</summary>
    Task<string?> ReadAccessTokenAsync(CancellationToken ct = default);

    /// <summary>The stored refresh token, or null when there is none.</summary>
    Task<string?> ReadRefreshTokenAsync(CancellationToken ct = default);

    /// <summary>Stores a new pair, replacing both tokens.</summary>
    Task WriteAsync(TokenPair pair, CancellationToken ct = default);

    /// <summary>Removes both tokens.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
