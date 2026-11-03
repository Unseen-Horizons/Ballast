// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// One sign-in link that was issued: who it is for, what it is for, the hash of its secret, and when it expires and was used. The secret
/// itself is never stored, only its SHA-256, so a copy of the table cannot be used to sign in.
/// </summary>
public sealed class AuthEmailLink
{
    /// <summary>The row's id.</summary>
    public int Id { get; set; }

    /// <summary>The subject the link signs in, as the app names it (for example a contact's id, as text).</summary>
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>What the link is for, such as <c>magic_link</c>. A link redeems only for its own purpose.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>The base64 SHA-256 of the link's secret. Unique.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>When the link was issued, in UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the link stops working, in UTC.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When the link was used, in UTC, or null while it is unused. A used link never works again.</summary>
    public DateTime? ClaimedAt { get; set; }
}
