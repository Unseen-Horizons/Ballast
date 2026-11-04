// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Data;

using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer;

/// <summary>
/// Runs a change in one database transaction under the context's execution strategy, and keeps the change tracker in step with
/// the database when the strategy retries it.
/// <para>
/// A context with a retrying execution strategy (<c>EnableRetryOnFailure</c>) replays the whole delegate after a transient fault.
/// A rolled-back attempt leaves the tracker out of step with the database: rows it added are still tracked, and entities it
/// changed hold values the database never kept. A replay would then insert twice, or see "no change" on a value it must write
/// again. So a retry first runs <see cref="ResetForRetryAsync"/>. The first attempt is never reset.
/// </para>
/// <para>
/// The work runs more than once. It must be safe to run again: read what it needs inside the work, not from before it.
/// </para>
/// </summary>
public static class TransactionExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> in one transaction, or joins the transaction that is already open.
    /// </summary>
    /// <param name="db">The context. Required.</param>
    /// <param name="work">The change. It receives the cancellation token and must pass it on. Required.</param>
    /// <param name="isolationLevel">The isolation level for a transaction this call opens. Null uses the provider's default.</param>
    /// <param name="ct">Cancels the work, the retries and the commit.</param>
    /// <returns>A task that completes when the work has committed, or when it has failed for good.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="db"/> or <paramref name="work"/> is null.</exception>
    public static Task ExecuteInTransactionAsync(this DbContext db, Func<CancellationToken, Task> work,
        IsolationLevel? isolationLevel = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return db.ExecuteInTransactionAsync<bool>(async token =>
        {
            await work(token);
            return true;
        }, isolationLevel, ct);
    }

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction, or joins the transaction that is already open, and returns its result.
    /// </summary>
    /// <typeparam name="T">The type of the work's result.</typeparam>
    /// <param name="db">The context. Required.</param>
    /// <param name="work">The change. It receives the cancellation token and must pass it on. Required.</param>
    /// <param name="isolationLevel">The isolation level for a transaction this call opens. Null uses the provider's default.</param>
    /// <param name="ct">Cancels the work, the retries and the commit.</param>
    /// <returns>The result of the attempt that committed.</returns>
    /// <remarks>
    /// <para>
    /// <b>Joining.</b> When a transaction is already open on the context, the work runs inside it and this call neither opens,
    /// commits nor rolls back a transaction. The owner of that transaction decides. The isolation level passed here is then not
    /// applied: the open transaction keeps its own.
    /// </para>
    /// <para>
    /// <b>Retries.</b> A transient fault makes the strategy run the work again, from the top, after a reset of the tracker. Any
    /// row the caller added to the context before this call, and did not save, is detached by that reset. Add rows inside
    /// <paramref name="work"/>.
    /// </para>
    /// <para>
    /// <b>Failures.</b> A failure in the work rolls the transaction back. A failure that the strategy does not retry is thrown
    /// unchanged. When the strategy gives up on a transient fault, it throws <c>RetryLimitExceededException</c> with the last
    /// failure as its inner exception, which <see cref="SqlErrors"/> still finds through the chain.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="db"/> or <paramref name="work"/> is null.</exception>
    public static async Task<T> ExecuteInTransactionAsync<T>(this DbContext db, Func<CancellationToken, Task<T>> work,
        IsolationLevel? isolationLevel = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(work);

        if (db.Database.CurrentTransaction is not null)
        {
            return await work(ct);
        }

        var attempt = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            // A retry starts from the database. The first attempt keeps what the caller already tracked.
            if (++attempt > 1)
            {
                await db.ResetForRetryAsync(token);
            }

            await using var transaction = isolationLevel is { } level
                ? await db.Database.BeginTransactionAsync(level, token)
                : await db.Database.BeginTransactionAsync(token);

            var result = await work(token);
            await transaction.CommitAsync(token);
            return result;
        }, ct);
    }

    /// <summary>
    /// Puts the change tracker back in step with the database after a rolled-back attempt. Detaches every <see cref="EntityState.Added"/>
    /// entry, then reloads every other tracked entry (unchanged, modified or deleted) from the database. A reload detaches an entry
    /// whose row no longer exists, which covers a row the rolled-back attempt inserted and saved.
    /// </summary>
    /// <remarks>
    /// One query runs per tracked entity that has a key. A context that tracks thousands of rows should clear its tracker instead
    /// (<c>ChangeTracker.Clear</c>), knowing that a caller that still holds one of those entities then holds a detached copy.
    /// </remarks>
    /// <param name="db">The context. Required.</param>
    /// <param name="ct">Cancels the reloads.</param>
    /// <exception cref="ArgumentNullException"><paramref name="db"/> is null.</exception>
    public static async Task ResetForRetryAsync(this DbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Detached or EntityState.Added)
            {
                continue;
            }

            // An entry without a key cannot be reloaded by key. The retried paths do not track one.
            if (entry.Metadata.FindPrimaryKey() is null)
            {
                continue;
            }

            await entry.ReloadAsync(ct);
        }
    }
}
