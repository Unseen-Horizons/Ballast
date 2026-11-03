// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The one thing the sign-in library needs from an app's user beyond what Identity provides: the name the access token carries
/// (the <c>given_name</c> claim, which the phone clients show). An app's user type implements it.
/// </summary>
public interface IAuthProfile
{
    /// <summary>The user's display name. Empty when the app has none; the token then carries an empty name.</summary>
    string FullName { get; set; }
}
