// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>Who may sign in with a password. Microsoft and Google sign-in are not affected by it.</summary>
public enum AuthPasswordSignInMode
{
    /// <summary>Nobody signs in with a password. Refused before any account is looked up.</summary>
    Off,

    /// <summary>Only accounts in <see cref="AuthOptions.PrivilegedRoleName"/> may sign in with a password.</summary>
    OwnerOnly,

    /// <summary>Every active account that has a password may sign in with one.</summary>
    All,
}
