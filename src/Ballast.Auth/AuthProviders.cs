// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The names of the external sign-in providers as they are stored in Identity's login table. They are stored data: an
/// existing row keeps its provider name, so these never change.
/// </summary>
public static class AuthProviders
{
    /// <summary>The Google sign-in. The login's key is Google's stable subject, never an address.</summary>
    public const string Google = "Google";

    /// <summary>The Microsoft staff sign-in. The login's key is the person's Microsoft object id.</summary>
    public const string Microsoft = "Microsoft";
}
