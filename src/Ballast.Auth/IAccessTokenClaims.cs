// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

namespace Ballast.Auth;

/// <summary>
/// Extra claims an app adds to every access token it issues, beyond the standard ones (subject, address, name, roles and the security
/// stamp). An app with none registers nothing: the library then adds an empty set. An app that needs one, such as a sign-in kind,
/// registers its implementation before <see cref="AuthRegistration.AddBallastAuth{TUser,TKey,TRole,TContext,TRefresh}"/>.
/// </summary>
/// <typeparam name="TUser">The app's user type.</typeparam>
public interface IAccessTokenClaims<in TUser>
{
    /// <summary>The extra claims for one user's token.</summary>
    /// <param name="user">The user the token is for.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The claims. Never null; an empty list adds none.</returns>
    Task<IReadOnlyList<Claim>> ClaimsForAsync(TUser user, CancellationToken ct = default);
}
