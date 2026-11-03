// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

namespace Ballast.Auth;

/// <summary>The extra claims of an app that registers none: an empty set, so the token carries only the standard claims.</summary>
/// <typeparam name="TUser">The app's user type.</typeparam>
internal sealed class NoAccessTokenClaims<TUser> : IAccessTokenClaims<TUser>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Claim>> ClaimsForAsync(TUser user, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Claim>>([]);
}
