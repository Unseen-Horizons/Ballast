// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>A session the library has issued or rotated: the access token, and the raw refresh token the client keeps.</summary>
/// <param name="AccessToken">The signed access token.</param>
/// <param name="RefreshToken">The raw refresh token. It is given once; only its hash is stored.</param>
/// <param name="RefreshTokenExpiresAtUtc">When the refresh token expires, in UTC.</param>
public sealed record IssuedSession(string AccessToken, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);
