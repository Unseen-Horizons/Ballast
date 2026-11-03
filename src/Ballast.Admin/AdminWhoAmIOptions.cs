// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Admin;

/// <summary>
/// The claim types <see cref="AdminWhoAmI"/> reads from the signed-in person. The defaults are the names a token keeps when the
/// pipeline does not map inbound claims (<c>MapInboundClaims = false</c>). An app whose pipeline maps them sets the mapped names.
/// </summary>
public sealed class AdminWhoAmIOptions
{
    /// <summary>The claim that holds the person's id. The default is <c>sub</c>.</summary>
    public string SubjectClaim { get; set; } = "sub";

    /// <summary>The claim that holds the person's display name. The default is <c>name</c>.</summary>
    public string NameClaim { get; set; } = "name";

    /// <summary>The claim that holds the person's email address. The default is <c>email</c>.</summary>
    public string EmailClaim { get; set; } = "email";

    /// <summary>The claim that holds the person's roles, one claim per role. The default is <c>role</c>.</summary>
    public string RoleClaim { get; set; } = "role";

    /// <summary>
    /// The claim that holds the person's permissions, one claim per permission. The default is <c>permission</c>. The app adds these to the
    /// principal on each request, so they are read from the principal and not from the token.
    /// </summary>
    public string PermissionClaim { get; set; } = "permission";

    /// <summary>
    /// The role that gives full access, matched without case. When the person holds it, <see cref="AdminWhoAmIResponse.FullAccess"/> is true.
    /// The default is null, which means no role gives full access and the flag is always false.
    /// </summary>
    public string? FullAccessRole { get; set; }
}
