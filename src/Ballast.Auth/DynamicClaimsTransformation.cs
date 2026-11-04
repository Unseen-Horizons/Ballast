// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Ballast.Auth;

/// <summary>
/// Replaces the role and permission claims carried in a token with what the database says now, so a revoked role or permission stops
/// working without waiting for the access token to expire. Each user's answer is cached for the times in <see cref="DynamicClaimsOptions"/>,
/// and the app evicts it with <see cref="DynamicClaimsCache"/> after it changes a role or an account.
/// <para>
/// Register it with <see cref="DynamicClaimsRegistration"/>, after <c>AddBallastAuth</c>. The cache must be one the app keeps for the
/// whole process, because the eviction runs on the same instance. A size-limited cache is safe: every entry declares a size of 1.
/// </para>
/// <para>
/// A token that is validly signed but names an account the store no longer has comes back as an unauthenticated principal, so the
/// access check refuses it, rather than as an authenticated one with its roles stripped.
/// </para>
/// </summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type: <c>string</c> for a GUID, or <c>int</c>.</typeparam>
/// <typeparam name="TRole">The app's Identity role.</typeparam>
public class DynamicClaimsTransformation<TUser, TKey, TRole> : IClaimsTransformation
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
    where TRole : IdentityRole<TKey>
{
    private readonly UserManager<TUser> _userManager;
    private readonly RoleManager<TRole> _roleManager;
    private readonly IMemoryCache _cache;
    private readonly DynamicClaimsOptions _options;

    private sealed record CachedClaims(List<string> Roles, List<string> Permissions);

    /// <summary>Creates the transformation.</summary>
    /// <param name="userManager">The user manager, which reads a user's roles.</param>
    /// <param name="roleManager">The role manager, which reads a role's permission claims.</param>
    /// <param name="cache">The process-wide cache the claims are kept in.</param>
    /// <param name="options">The claim names and cache times. Null for the defaults.</param>
    /// <exception cref="ArgumentNullException">A manager or the cache is null.</exception>
    /// <exception cref="InvalidOperationException">The options are out of range. See <see cref="DynamicClaimsOptions.Validate"/>.</exception>
    public DynamicClaimsTransformation(
        UserManager<TUser> userManager,
        RoleManager<TRole> roleManager,
        IMemoryCache cache,
        DynamicClaimsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(roleManager);
        ArgumentNullException.ThrowIfNull(cache);

        _userManager = userManager;
        _roleManager = roleManager;
        _cache = cache;
        _options = options ?? new DynamicClaimsOptions();
        _options.Validate();
    }

    /// <inheritdoc />
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        var userId = principal.FindFirst(_options.SubjectClaimType)?.Value;
        if (userId is null)
        {
            return principal;
        }

        var identity = (ClaimsIdentity)principal.Identity!;

        var oldRoleClaims = identity.FindAll(_options.RoleClaimType).ToList();
        var oldPermissionClaims = identity.FindAll(_options.PermissionClaimType).ToList();

        foreach (var c in oldRoleClaims.Concat(oldPermissionClaims))
        {
            identity.RemoveClaim(c);
        }

        var cached = await _cache.GetOrCreateAsync(DynamicClaimsCache.KeyFor(userId), async entry =>
        {
            entry.SlidingExpiration = _options.SlidingExpiration;
            entry.AbsoluteExpirationRelativeToNow = _options.AbsoluteExpiration;

            // Harmless where the cache has no size limit, and required where it does: a size-limited cache throws on any entry without a size.
            entry.Size = 1;

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return null;
            }

            var roles = (await _userManager.GetRolesAsync(user)).ToList();
            var permissions = new List<string>();

            foreach (var roleName in roles)
            {
                var roleEntity = await _roleManager.FindByNameAsync(roleName);
                if (roleEntity is not null)
                {
                    var roleClaims = await _roleManager.GetClaimsAsync(roleEntity);
                    permissions.AddRange(roleClaims.Where(c => c.Type == _options.PermissionClaimType).Select(c => c.Value));
                }
            }

            return new CachedClaims(roles, permissions);
        });

        if (cached is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        foreach (var role in cached.Roles)
        {
            identity.AddClaim(new Claim(_options.RoleClaimType, role));
        }

        foreach (var permission in cached.Permissions)
        {
            identity.AddClaim(new Claim(_options.PermissionClaimType, permission));
        }

        return principal;
    }
}
