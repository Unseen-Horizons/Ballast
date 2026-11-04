// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Auth;

/// <summary>What the Google sign-in needs from the app.</summary>
/// <param name="WebClientId">
/// The Google Web client id. The server checks the ID token's audience against it, so it must be the id the server is configured with.
/// Null or blank turns the button off.
/// </param>
public sealed record GoogleSignInOptions(string? WebClientId);
