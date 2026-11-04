// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The settings of <see cref="DynamicClaimsTransformation{TUser,TKey,TRole}"/>: the claim names it reads and writes, and how long a user's
/// roles and permissions stay cached. The defaults are the names the sign-in library's tokens use, and the cache times the library ships with.
/// </summary>
public sealed class DynamicClaimsOptions
{
    /// <summary>The claim that carries the user's key in the token. Default <c>sub</c>, which <see cref="AuthClaimNames.Subject"/> also defaults to.</summary>
    public string SubjectClaimType { get; set; } = "sub";

    /// <summary>The claim that carries one role. Default <c>role</c>, which <see cref="AuthClaimNames.Role"/> also defaults to. Set both to the same name.</summary>
    public string RoleClaimType { get; set; } = "role";

    /// <summary>The claim that carries one permission of a role. Default <c>permission</c>.</summary>
    public string PermissionClaimType { get; set; } = "permission";

    /// <summary>
    /// How long a user's cached roles and permissions last after their last use. Default 30 seconds. It must not exceed
    /// <see cref="AbsoluteExpiration"/>.
    /// </summary>
    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest a user's cached roles and permissions last, however often they are used. Default 5 minutes. Sliding alone would never
    /// lapse for a user who keeps making requests, so a revoked permission would stay live.
    /// </summary>
    public TimeSpan AbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Checks the options.</summary>
    /// <exception cref="InvalidOperationException">A claim name is empty, a cache time is not positive, or the sliding time exceeds the absolute time. The message names the setting.</exception>
    public void Validate()
    {
        RequireClaimType(SubjectClaimType, nameof(SubjectClaimType));
        RequireClaimType(RoleClaimType, nameof(RoleClaimType));
        RequireClaimType(PermissionClaimType, nameof(PermissionClaimType));

        if (SlidingExpiration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(SlidingExpiration)} must be positive.");
        }

        if (AbsoluteExpiration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(AbsoluteExpiration)} must be positive.");
        }

        if (SlidingExpiration > AbsoluteExpiration)
        {
            throw new InvalidOperationException($"{nameof(SlidingExpiration)} must not exceed {nameof(AbsoluteExpiration)}, which is the ceiling.");
        }
    }

    private static void RequireClaimType(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is not set.");
        }
    }
}
