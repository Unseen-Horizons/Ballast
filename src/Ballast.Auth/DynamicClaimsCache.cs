// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Ballast.Auth;

/// <summary>
/// The one owner of the claims-cache key and of evicting it. A role's permission set is rewritten in the database, but
/// <see cref="DynamicClaimsTransformation{TUser,TKey,TRole}"/> keeps each user's roles and permissions for a while (see
/// <see cref="DynamicClaimsOptions"/>). Without an eviction a revoked permission stays live, and a granted one stays missing, for that long.
/// Call an eviction after the change has committed, so a request that misses the cache cannot read the old set again.
/// </summary>
public static class DynamicClaimsCache
{
    /// <summary>The cache key under which <see cref="DynamicClaimsTransformation{TUser,TKey,TRole}"/> stores a user's claims.</summary>
    /// <param name="userId">The user's key as the token's subject carries it.</param>
    /// <returns>The key.</returns>
    public static string KeyFor(string userId)
    {
        ArgumentNullException.ThrowIfNull(userId);
        return $"claims:{userId}";
    }

    /// <summary>Drops one user's cached claims. Their next request reads the database.</summary>
    /// <param name="cache">The cache the transformation uses.</param>
    /// <param name="userId">The user's key as the token's subject carries it.</param>
    public static void Evict(IMemoryCache cache, string userId)
    {
        ArgumentNullException.ThrowIfNull(cache);
        cache.Remove(KeyFor(userId));
    }

    /// <summary>
    /// Drops the cached claims of every user holding <paramref name="roleName"/>. Call it after the role's permission change has committed.
    /// </summary>
    /// <typeparam name="TUser">The user.</typeparam>
    /// <typeparam name="TKey">The key.</typeparam>
    /// <param name="cache">The cache the transformation uses.</param>
    /// <param name="users">The user manager that lists the role's members.</param>
    /// <param name="roleName">The role whose members are evicted.</param>
    public static async Task EvictRoleMembersAsync<TUser, TKey>(IMemoryCache cache, UserManager<TUser> users, string roleName)
        where TUser : IdentityUser<TKey>
        where TKey : IEquatable<TKey>
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(roleName);

        foreach (var member in await users.GetUsersInRoleAsync(roleName))
        {
            Evict(cache, AuthKey.Text(member.Id));
        }
    }
}
