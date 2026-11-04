// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>What a password sign-in takes to find the account: the e-mail address, or the user name.</summary>
public enum AuthPasswordSignInIdentifier
{
    /// <summary>The account's e-mail address. The default, and the behaviour before this option existed.</summary>
    Email,

    /// <summary>
    /// The account's user name, as Identity stores it. For an app whose people sign in with a name rather than an address. The lookup, the
    /// lockout, the audit and the hashing time are the same as for an address.
    /// </summary>
    UserName,
}
