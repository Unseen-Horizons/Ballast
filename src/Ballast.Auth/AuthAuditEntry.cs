// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>One audit row the library records. The fields are the columns of a plain audit table.</summary>
/// <param name="UserId">The user the row is about, as text, or null when there is none.</param>
/// <param name="Action">What happened, such as <c>LoginFailed</c> or <c>TokenRefresh</c>.</param>
/// <param name="EntityType">The kind of thing the action touched, such as <c>User</c> or <c>RefreshToken</c>.</param>
/// <param name="EntityId">The id of that thing, as text.</param>
/// <param name="CreatedAtUtc">When the action happened, in UTC.</param>
/// <param name="OldValues">What the thing held before, or null.</param>
/// <param name="NewValues">What the thing holds now, or null.</param>
public sealed record AuthAuditEntry(
    string? UserId,
    string Action,
    string EntityType,
    string EntityId,
    DateTime CreatedAtUtc,
    string? OldValues = null,
    string? NewValues = null);
