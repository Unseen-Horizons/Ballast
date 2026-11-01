// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>
/// The table the send-once ledger writes to, implemented by the app over its own table. The library never names that table.
/// Each record is unique per (<see cref="SendOnceRecord.SubjectKey"/>, <see cref="SendOnceRecord.Kind"/>); the app enforces
/// that with a unique index, and maps the index's violation to <c>false</c> in <see cref="TryReserveAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The time column.</b> Each record has one time, <see cref="SendOnceRecord.CreatedAtUtc"/>, and the ledger uses it two ways.
/// A Failed record keeps its first time, so the retry window counts from the first reservation and a permanently failing address
/// becomes final. A Pending record takes the time its current attempt was claimed, so a stale send is measured from its claim.
/// <see cref="TryClaimAsync"/> therefore writes the time the claim carries, and the ledger sets that time.
/// </para>
/// <para>
/// <b>Failures.</b> A store that cannot do its work throws. Only the two <c>Try</c> methods return <c>false</c>, and only for the
/// reason their names give: another sender holds the record, or the record no longer matches.
/// </para>
/// </remarks>
public interface ISendOnceStore
{
    /// <summary>Finds the record for a subject and kind.</summary>
    /// <param name="subjectKey">The subject.</param>
    /// <param name="kind">The kind of e-mail.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The record, or null when the subject and kind have none.</returns>
    Task<SendOnceRecord?> FindAsync(string subjectKey, string kind, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new record in the state the record carries. Returns <c>false</c> when a record for the same subject and kind
    /// already exists, which is what the unique index reports when another sender reserved it first. Nothing else is changed.
    /// </summary>
    /// <param name="record">The record to insert.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns><c>true</c> when this caller inserted the record; <c>false</c> when another record already holds the key.</returns>
    Task<bool> TryReserveAsync(SendOnceRecord record, CancellationToken ct = default);

    /// <summary>
    /// A compare-and-set. When the record still holds the expected state and the expected time, it becomes Pending for the
    /// claim's recipient, with the claim's time. The store must test the state and the time in the same statement that writes
    /// them (a conditional update), so that of two claims only one can succeed.
    /// </summary>
    /// <param name="claim">The claim.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns><c>true</c> when this caller won the claim; <c>false</c> when the record no longer matches.</returns>
    Task<bool> TryClaimAsync(SendOnceClaim claim, CancellationToken ct = default);

    /// <summary>
    /// Sets the state of an existing record that this caller holds. Only the state changes; the recipient and the time stay.
    /// </summary>
    /// <param name="subjectKey">The subject.</param>
    /// <param name="kind">The kind of e-mail.</param>
    /// <param name="state">The state to set: Sent or Failed.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="InvalidOperationException">The record does not exist. The ledger only marks a record it reserved or claimed.</exception>
    Task MarkAsync(string subjectKey, string kind, SendOnceState state, CancellationToken ct = default);
}
