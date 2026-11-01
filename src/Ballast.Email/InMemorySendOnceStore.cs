// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>
/// A store that keeps its records in memory. It follows the contract of <see cref="ISendOnceStore"/> exactly, including the
/// compare-and-set in <see cref="TryClaimAsync"/>, so the ledger's rules can be tested against it.
/// </summary>
/// <remarks>
/// <b>For tests and local tools only.</b> The records are lost when the process ends, and they are not shared between processes,
/// so a production app must use a store over its database. The class is public so that an app's own tests can use it.
/// </remarks>
public sealed class InMemorySendOnceStore : ISendOnceStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string SubjectKey, string Kind), SendOnceRecord> _records = [];

    /// <inheritdoc />
    public Task<SendOnceRecord?> FindAsync(string subjectKey, string kind, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            SendOnceRecord? found = _records.TryGetValue((subjectKey, kind), out var record) ? record : null;
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryReserveAsync(SendOnceRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (record.SubjectKey, record.Kind);
            if (_records.ContainsKey(key))
            {
                return Task.FromResult(false);
            }

            _records[key] = record;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryClaimAsync(SendOnceClaim claim, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (claim.SubjectKey, claim.Kind);
            if (!_records.TryGetValue(key, out var current)
                || current.State != claim.ExpectedState
                || current.CreatedAtUtc != claim.ExpectedCreatedAtUtc)
            {
                return Task.FromResult(false);
            }

            _records[key] = new SendOnceRecord(claim.SubjectKey, claim.Kind, SendOnceState.Pending, claim.Recipient, claim.CreatedAtUtc);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task MarkAsync(string subjectKey, string kind, SendOnceState state, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (subjectKey, kind);
            if (!_records.TryGetValue(key, out var current))
            {
                return Task.FromException(new InvalidOperationException($"No send-once record exists for subject {subjectKey} and kind {kind}, so it cannot be marked {state}."));
            }

            _records[key] = current with { State = state };
            return Task.CompletedTask;
        }
    }
}
