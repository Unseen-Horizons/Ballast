// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs;

/// <summary>
/// One database session that can hold one application lock. The lock belongs to the session: it is freed by
/// <see cref="ReleaseAsync"/> and, whatever else happens, when the session ends. That second part is why the
/// session is a connection of its own and not a pooled one, and why a dropped connection is visible as a lost lock.
/// </summary>
public interface IAppLockSession : IAsyncDisposable
{
    /// <summary>Opens the session's connection.</summary>
    /// <param name="ct">Cancels the open.</param>
    Task OpenAsync(CancellationToken ct);

    /// <summary>
    /// Asks for the lock exclusively and waits up to <paramref name="timeout"/>. True when it was granted, false
    /// when another session still held it after the wait. Any other answer is an exception.
    /// </summary>
    /// <param name="resource">The lock's name.</param>
    /// <param name="timeout">How long the server may wait for the holder to let go.</param>
    /// <param name="ct">Cancels the request.</param>
    Task<bool> TryAcquireAsync(string resource, TimeSpan timeout, CancellationToken ct);

    /// <summary>True while this session still holds <paramref name="resource"/> exclusively.</summary>
    /// <param name="resource">The lock's name.</param>
    /// <param name="ct">Cancels the question.</param>
    Task<bool> HoldsAsync(string resource, CancellationToken ct);

    /// <summary>Gives the lock back. The session's connection stays open until it is disposed.</summary>
    /// <param name="resource">The lock's name.</param>
    /// <param name="ct">Cancels the release.</param>
    Task ReleaseAsync(string resource, CancellationToken ct);
}
