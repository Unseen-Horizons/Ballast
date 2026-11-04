// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;

namespace Ballast.Auth;

/// <summary>
/// The app's own profile of a Microsoft staff member: a table the app keeps beside Identity (for example a row per employee), whose key is the
/// account's key. The library writes the Identity tables. Each hook runs inside the same transaction as the Identity change it follows, so the
/// profile and the account are saved together or not at all. The default does nothing. Register an implementation before or after
/// <c>AddBallastMicrosoftSignIn</c>; the app's registration is used.
/// <para>
/// A hook that throws rolls back the change it belongs to. A Microsoft sign-in then throws <see cref="MicrosoftStaffProfileException"/> with the
/// hook's exception inside, and nothing is saved. The directory sync lists that person as failed (<c>PROFILE_HOOK_FAILED</c>), rolls back only that
/// person's change, and goes on with the others.
/// </para>
/// </summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public interface IMicrosoftStaffProfile<TUser, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    /// <summary>
    /// Runs before the account is inserted, in the transaction that inserts it. The app may write its profile row here, in the same
    /// <c>DbContext</c> and saved there, and set <paramref name="user"/>'s <c>Id</c> from that row, so the account and the profile share one key.
    /// A profile whose key the database generates is saved first, then its key is copied to the user.
    /// </summary>
    /// <param name="user">The account about to be inserted, with its name, address and key.</param>
    /// <param name="person">What Microsoft gave for the person.</param>
    /// <param name="ct">Cancels the call.</param>
    Task CreatingAsync(TUser user, MicrosoftPerson person, CancellationToken ct);

    /// <summary>
    /// Runs after a name, address or role change of a linked account, in the transaction that makes it. A sign-in and the directory sync both
    /// call it. It does not run when only the switch-on changed.
    /// </summary>
    /// <param name="user">The account, after the change.</param>
    /// <param name="person">What Microsoft gives now for the person.</param>
    /// <param name="ct">Cancels the call.</param>
    Task AppliedAsync(TUser user, MicrosoftPerson person, CancellationToken ct);

    /// <summary>Runs after the account is switched off by the directory sync, in the transaction that switches it off.</summary>
    /// <param name="user">The account, after it was switched off.</param>
    /// <param name="ct">Cancels the call.</param>
    Task SwitchedOffAsync(TUser user, CancellationToken ct);
}

/// <summary>The default: the app keeps no profile, so the hooks do nothing.</summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class NoMicrosoftStaffProfile<TUser, TKey> : IMicrosoftStaffProfile<TUser, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    /// <inheritdoc />
    public Task CreatingAsync(TUser user, MicrosoftPerson person, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task AppliedAsync(TUser user, MicrosoftPerson person, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SwitchedOffAsync(TUser user, CancellationToken ct) => Task.CompletedTask;
}
