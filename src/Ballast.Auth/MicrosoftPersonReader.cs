// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

namespace Ballast.Auth;

/// <summary>What a Microsoft ID token says about one person, after the checks.</summary>
/// <param name="ObjectId">The person's Microsoft object id, in the standard form. It is the login key.</param>
/// <param name="Address">The sign-in address, lower case.</param>
/// <param name="GivenName">The given name.</param>
/// <param name="Surname">The family name.</param>
/// <param name="RoleValues">The app role values, distinct, as Microsoft gives them.</param>
public sealed record MicrosoftPerson(string ObjectId, string Address, string GivenName, string Surname, IReadOnlyList<string> RoleValues);

/// <summary>Reads a Microsoft ID token's claims, and refuses a token that is not this tenant's or lacks what an account needs.</summary>
internal static class MicrosoftPersonReader
{
    /// <summary>Reads the person from a validated principal.</summary>
    /// <param name="principal">The principal the handshake produced.</param>
    /// <param name="options">The settings, for the tenant.</param>
    /// <returns>The person, or the refusal when there is none.</returns>
    public static (MicrosoftPerson? Person, string? Refusal) Read(ClaimsPrincipal principal, MicrosoftSignInOptions options)
    {
        // A token from another tenant is refused before anything else is read from it.
        if (!string.Equals(principal.FindFirst("tid")?.Value, options.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return (null, MicrosoftRefusal.WrongTenant);
        }

        if (!Guid.TryParse(principal.FindFirst("oid")?.Value, out var objectId))
        {
            return (null, MicrosoftRefusal.SignInFailed);
        }

        // preferred_username is the sign-in name; email is the fallback for a token that has none.
        var address = NormalizeAddress(principal.FindFirst("preferred_username")?.Value)
            ?? NormalizeAddress(principal.FindFirst("email")?.Value);
        if (address is null)
        {
            return (null, MicrosoftRefusal.SignInFailed);
        }

        // The names come from the given_name and family_name claims. The display name is never split to guess them.
        var givenName = (principal.FindFirst("given_name")?.Value ?? string.Empty).Trim();
        var surname = (principal.FindFirst("family_name")?.Value ?? string.Empty).Trim();
        if (givenName.Length == 0 || surname.Length == 0)
        {
            return (null, MicrosoftRefusal.NameMissing);
        }

        var roleValues = principal.FindAll("roles")
            .Select(claim => claim.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (new MicrosoftPerson(objectId.ToString("D"), address, givenName, surname, roleValues), null);
    }

    /// <summary>The address in the form the library compares: trimmed and lower case. Null or blank gives null.</summary>
    /// <param name="address">The address as Microsoft gave it.</param>
    /// <returns>The normalised address, or null.</returns>
    public static string? NormalizeAddress(string? address)
    {
        var normalized = (address ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 ? null : normalized;
    }
}
