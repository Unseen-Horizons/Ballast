// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Admin;

/// <summary>
/// The answer to "who am I" for a staff console: the signed-in person, their roles and permissions, and whether a role gives full access.
/// The console reads it to decide which navigation to show. It is for display and navigation only: the server still checks every
/// permission on each action.
/// </summary>
/// <param name="Subject">The person's id, or null when the principal has no subject claim.</param>
/// <param name="Name">The person's display name, or null when the principal has no name claim.</param>
/// <param name="Email">The person's email address, or null when the principal has no email claim.</param>
/// <param name="Roles">The person's roles, distinct and sorted by ordinal comparison. Empty when there are none.</param>
/// <param name="Permissions">The person's permissions, distinct and sorted by ordinal comparison. Empty when there are none.</param>
/// <param name="FullAccess">True when the person holds the role named by <see cref="AdminWhoAmIOptions.FullAccessRole"/>.</param>
public sealed record AdminWhoAmIResponse(
    string? Subject,
    string? Name,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool FullAccess);
