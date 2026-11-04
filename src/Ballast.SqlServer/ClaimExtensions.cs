// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Ballast.SqlServer;

/// <summary>
/// The claim pattern: one conditional <c>UPDATE</c> that moves a row from the state the caller expects to a new state, and says
/// whether this caller was the one that moved it. Two requests that race for the same row cannot both win, because the
/// second finds the row no longer in the expected state.
/// </summary>
public static class ClaimExtensions
{
    /// <summary>
    /// Runs the conditional update and reports whether it changed the row.
    /// </summary>
    /// <typeparam name="TEntity">The entity the query selects.</typeparam>
    /// <param name="claim">
    /// The query that names the row and the state it expects, for example
    /// <c>db.Orders.Where(o =&gt; o.Id == id &amp;&amp; o.Status == OrderStatus.Pending)</c>. It must name the key, so that it matches at most one
    /// row. Required.
    /// </param>
    /// <param name="changes">The new values, as <c>ExecuteUpdateAsync</c> takes them. Required.</param>
    /// <param name="ct">Cancels the update.</param>
    /// <returns>
    /// True when this call changed the row (one row matched). False when no row matched: another request changed it first, or the row
    /// is not in the expected state. The caller decides what that means. A false answer never overwrites anything.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A claim that changes more than one row is a bug in the query, and the helper throws
    /// <see cref="InvalidOperationException"/>. Outside a transaction, the rows it changed are already written when the exception
    /// is thrown. Run a claim inside a transaction (for example <see cref="TransactionExtensions.ExecuteInTransactionAsync{T}"/>, or a
    /// transaction the caller opened) when a rollback must undo it.
    /// </para>
    /// <para>
    /// The helper cannot check that the query is conditional. The WHERE clause is the caller's; a claim without a state condition
    /// overwrites the row.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="claim"/> or <paramref name="changes"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The update changed more than one row.</exception>
    public static async Task<bool> ClaimAsync<TEntity>(this IQueryable<TEntity> claim,
        Action<UpdateSettersBuilder<TEntity>> changes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(changes);

        var rows = await claim.ExecuteUpdateAsync(changes, ct);
        return rows switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException(
                $"A claim changed {rows} rows. A claim must name one row by its key and the state it expects, so that it matches at most one row."),
        };
    }
}
