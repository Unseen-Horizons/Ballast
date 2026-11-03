// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;

namespace Ballast.Auth;

/// <summary>
/// Keeps "no such account" from being measurably faster than "wrong password". Verifying a password costs a deliberately slow hash, so
/// an early answer for an unknown address is measurable over the network, and the sign-in becomes a test of whether an address has an
/// account. Hashing a throwaway password on the miss path spends the same time before the same refusal.
/// </summary>
public static class AccountEnumerationGuard
{
    /// <summary>
    /// Spends the work a real password check would, so an unknown, locked or password-less account does not answer faster than a known one.
    /// The hash is deliberately discarded.
    /// </summary>
    /// <typeparam name="TUser">The user type. It needs a parameterless constructor, as Identity's do.</typeparam>
    /// <param name="userManager">The user manager, whose hasher does the work.</param>
    /// <param name="password">The password the caller sent. It is hashed and then dropped.</param>
    public static void SpendPasswordHashingTime<TUser>(UserManager<TUser> userManager, string password)
        where TUser : class, new()
    {
        ArgumentNullException.ThrowIfNull(userManager);
        _ = userManager.PasswordHasher.HashPassword(new TUser(), password);
    }
}
