// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The names of the claims in an access token. The defaults are the standard names that clients read (<c>sub</c>,
/// <c>email</c>, <c>given_name</c>), An app that shipped other names sets them here.
/// </summary>
public sealed class AuthClaimNames
{
    /// <summary>The subject: the user's id. Default <c>sub</c>.</summary>
    public string Subject { get; set; } = "sub";

    /// <summary>The user's address. Default <c>email</c>.</summary>
    public string Email { get; set; } = "email";

    /// <summary>The user's display name, read by the client as the name. Default <c>given_name</c>.</summary>
    public string GivenName { get; set; } = "given_name";

    /// <summary>One claim per role the user holds. Default <c>role</c>.</summary>
    public string Role { get; set; } = "role";

    /// <summary>The security stamp the token was issued with. Default <c>security_stamp</c>.</summary>
    public string SecurityStamp { get; set; } = "security_stamp";
}
