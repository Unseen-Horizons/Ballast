// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The app's emergency accounts: password accounts kept for the day Microsoft is unavailable. The library cannot tell which accounts these are,
/// so the app says so by registering an implementation. The directory sync never switches one off, never applies a change to one, and leaves
/// one out of its looks-wrong check. Microsoft sign-in never links one to a Microsoft login and never changes it.
/// <para>
/// The default, when the app registers none, names no account. Register your own before or after <c>AddBallastMicrosoftSignIn</c>; the last
/// registration wins.
/// </para>
/// </summary>
/// <typeparam name="TKey">The key type of the app's accounts.</typeparam>
public interface IMicrosoftEmergencyAccounts<TKey>
{
    /// <summary>The keys of the emergency accounts. Read at each sync run and at each Microsoft sign-in.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The keys. An empty set names none.</returns>
    Task<IReadOnlySet<TKey>> IdsAsync(CancellationToken ct);
}

/// <summary>The default: no account is an emergency account.</summary>
/// <typeparam name="TKey">The key type of the app's accounts.</typeparam>
internal sealed class NoMicrosoftEmergencyAccounts<TKey> : IMicrosoftEmergencyAccounts<TKey>
{
    /// <inheritdoc />
    public Task<IReadOnlySet<TKey>> IdsAsync(CancellationToken ct) => Task.FromResult<IReadOnlySet<TKey>>(new HashSet<TKey>());
}
