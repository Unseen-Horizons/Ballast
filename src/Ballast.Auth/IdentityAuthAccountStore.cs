// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;

namespace Ballast.Auth;

/// <summary>
/// The account store over ASP.NET Core Identity: the stamp is the user's <c>SecurityStamp</c>, and the account may sign in when it is not
/// locked out. Identity's <see cref="UserManager{TUser}.FindByIdAsync"/> converts the text id to the key type, so the same code serves a
/// string key and an int key.
/// </summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type: a string (GUID text) or an int.</typeparam>
public sealed class IdentityAuthAccountStore<TUser, TKey> : IAuthAccountStore
    where TUser : IdentityUser<TKey>, IAuthProfile
    where TKey : IEquatable<TKey>
{
    private readonly UserManager<TUser> _users;

    /// <summary>Creates the store over the user manager.</summary>
    /// <param name="users">The user manager of the app's Identity user.</param>
    public IdentityAuthAccountStore(UserManager<TUser> users)
    {
        _users = users;
    }

    /// <inheritdoc />
    public async Task<AuthAccountState?> FindAsync(string subject, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var user = await _users.FindByIdAsync(subject);
        if (user is null)
        {
            return null;
        }

        var locked = await _users.IsLockedOutAsync(user);
        return new AuthAccountState(user.SecurityStamp ?? string.Empty, !locked);
    }
}
