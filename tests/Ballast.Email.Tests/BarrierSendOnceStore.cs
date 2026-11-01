// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email.Tests;

/// <summary>
/// Wraps a store so that each caller's <see cref="FindAsync"/> waits until every party has read. Two senders therefore both see
/// the same record before either writes, which is the race the store's conditional writes must settle.
/// </summary>
public sealed class BarrierSendOnceStore(ISendOnceStore inner, int parties) : ISendOnceStore
{
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    /// <inheritdoc />
    public async Task<SendOnceRecord?> FindAsync(string subjectKey, string kind, CancellationToken ct = default)
    {
        var found = await inner.FindAsync(subjectKey, kind, ct);
        if (Interlocked.Increment(ref _arrived) == parties)
        {
            _allArrived.TrySetResult();
        }

        await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        return found;
    }

    /// <inheritdoc />
    public Task<bool> TryReserveAsync(SendOnceRecord record, CancellationToken ct = default) => inner.TryReserveAsync(record, ct);

    /// <inheritdoc />
    public Task<bool> TryClaimAsync(SendOnceClaim claim, CancellationToken ct = default) => inner.TryClaimAsync(claim, ct);

    /// <inheritdoc />
    public Task MarkAsync(string subjectKey, string kind, SendOnceState state, CancellationToken ct = default) => inner.MarkAsync(subjectKey, kind, state, ct);
}
