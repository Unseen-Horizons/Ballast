// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Auth;

/// <summary>An access token and the refresh token that goes with it.</summary>
/// <param name="AccessToken">The short-lived bearer token.</param>
/// <param name="RefreshToken">The token that buys a new access token. The server rotates it on every use.</param>
public sealed record TokenPair(string AccessToken, string RefreshToken);
