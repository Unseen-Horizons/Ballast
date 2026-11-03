// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

namespace Ballast.Admin;

/// <summary>
/// Builds the answer to "who am I" from the signed-in person's claims. An app maps it with a thin controller action that returns
/// <see cref="Create"/>'s result as JSON, and answers 401 when it is null.
/// </summary>
public static class AdminWhoAmI
{
    /// <summary>
    /// Reads the signed-in person from a principal. Blank claim values are ignored, and the roles and permissions are distinct and sorted.
    /// </summary>
    /// <param name="principal">The principal of the request. Required.</param>
    /// <param name="options">The claim types to read, or null for the defaults.</param>
    /// <returns>The answer, or null when the principal is not authenticated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="principal"/> is null.</exception>
    /// <exception cref="ArgumentException">A claim type in <paramref name="options"/> is blank.</exception>
    public static AdminWhoAmIResponse? Create(ClaimsPrincipal principal, AdminWhoAmIOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        options ??= new AdminWhoAmIOptions();
        Validate(options);

        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var roles = Values(principal, options.RoleClaim);
        var fullAccess = !string.IsNullOrWhiteSpace(options.FullAccessRole)
            && roles.Contains(options.FullAccessRole, StringComparer.OrdinalIgnoreCase);

        return new AdminWhoAmIResponse(
            Subject: FirstValue(principal, options.SubjectClaim),
            Name: FirstValue(principal, options.NameClaim),
            Email: FirstValue(principal, options.EmailClaim),
            Roles: roles,
            Permissions: Values(principal, options.PermissionClaim),
            FullAccess: fullAccess);
    }

    private static void Validate(AdminWhoAmIOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SubjectClaim, nameof(AdminWhoAmIOptions.SubjectClaim));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.NameClaim, nameof(AdminWhoAmIOptions.NameClaim));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.EmailClaim, nameof(AdminWhoAmIOptions.EmailClaim));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RoleClaim, nameof(AdminWhoAmIOptions.RoleClaim));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PermissionClaim, nameof(AdminWhoAmIOptions.PermissionClaim));
    }

    private static string? FirstValue(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirst(claimType)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static List<string> Values(ClaimsPrincipal principal, string claimType)
    {
        return principal.FindAll(claimType)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }
}
