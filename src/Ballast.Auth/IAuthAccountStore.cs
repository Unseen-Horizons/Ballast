// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// Where the library asks, on every request, whether a signed access token's account still stands. It answers for any account the app
/// signs in, whether the account lives in Identity (<see cref="IdentityAuthAccountStore{TUser,TKey}"/>) or in the app's own table, such as a
/// contact who signs in by e-mail link and has no password. The app implements it for its own table.
/// </summary>
/// <remarks>
/// The answer is the instant cut-off: a change of the stamp, a lockout, or a deactivation refuses the account's access tokens at once,
/// without waiting for them to expire. The token's stamp is compared with the stamp returned here. An implementation reads the account
/// with one indexed lookup, because it runs on every request that carries a token.
/// </remarks>
public interface IAuthAccountStore
{
    /// <summary>Finds an account's current stamp and whether it may sign in now.</summary>
    /// <param name="subject">The subject of the token: the account's id, as text.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The account's state, or null when no such account exists.</returns>
    Task<AuthAccountState?> FindAsync(string subject, CancellationToken ct = default);
}

/// <summary>The part of an account the access check needs.</summary>
/// <param name="SecurityStamp">The account's current security stamp. A token carrying another stamp is refused.</param>
/// <param name="CanSignIn">
/// Whether the account may sign in now. False for a locked or a deactivated account, and its access tokens are then refused too.
/// </param>
public sealed record AuthAccountState(string SecurityStamp, bool CanSignIn);
