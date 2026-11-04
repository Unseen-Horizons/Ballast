// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The app's own rows for an account the library creates, such as a wallet. It runs inside the creation transaction, after the user, its role
/// and its login are added and before the save, so the rows are created or rolled back together with the account.
/// </summary>
/// <remarks>
/// The implementation adds to the app's context and does not save: the library saves. Without an implementation the account has no
/// app rows, which is correct for an app that keeps none.
/// </remarks>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
public interface IAuthNewAccount<in TUser>
    where TUser : class
{
    /// <summary>Adds the app's rows for a new account, to be saved with it.</summary>
    /// <param name="user">The account, already added with its role and login.</param>
    /// <param name="ct">Cancels the call.</param>
    Task AddRowsAsync(TUser user, CancellationToken ct = default);
}

/// <summary>The default account hook: adds nothing.</summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
internal sealed class NoAuthNewAccount<TUser> : IAuthNewAccount<TUser>
    where TUser : class
{
    /// <inheritdoc />
    public Task AddRowsAsync(TUser user, CancellationToken ct = default) => Task.CompletedTask;
}
