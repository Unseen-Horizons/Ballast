// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

namespace Ballast.Auth.Tests;

/// <summary>An app's extra claims for the tests: one fixed claim on every token.</summary>
/// <typeparam name="TUser">The user type.</typeparam>
internal sealed class FixedClaims<TUser> : IAccessTokenClaims<TUser>
{
    /// <summary>The claim type the tests look for.</summary>
    public const string ClaimType = "auth_kind";

    /// <summary>The claim value the tests look for.</summary>
    public const string ClaimValue = "test-kind";

    /// <inheritdoc />
    public Task<IReadOnlyList<Claim>> ClaimsForAsync(TUser user, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Claim>>([new Claim(ClaimType, ClaimValue)]);
}
