// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer;

/// <summary>
/// Reads the next value of a SQL Server sequence.
/// </summary>
public static class SequenceExtensions
{
    /// <summary>
    /// Returns the next value of a sequence (<c>SELECT NEXT VALUE FOR</c>). Each call takes a new value; two callers never get the
    /// same one. A sequence value is not rolled back with a transaction, so a gap in the numbers is normal.
    /// </summary>
    /// <typeparam name="T">
    /// The type the value is read as. It must match the sequence's type: a <c>bigint</c> sequence is read as <c>long</c>.
    /// </typeparam>
    /// <param name="db">The context. Required.</param>
    /// <param name="sequenceName">
    /// The sequence's name: <c>Name</c>, or <c>Schema.Name</c>. Letters, digits and underscores only, not starting with a digit,
    /// at most 128 characters a part. The name is checked and bracket-quoted; it is never written into the statement as given.
    /// </param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The next value.</returns>
    /// <remarks>
    /// The statement is only enumerated, never composed into a larger query. SQL Server refuses <c>NEXT VALUE FOR</c> inside a
    /// subquery, and <c>First</c> or <c>Single</c> would wrap the query in one.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="db"/> or <paramref name="sequenceName"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="sequenceName"/> is empty, malformed, or not a plain identifier. No SQL is sent.</exception>
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">The sequence does not exist, or the server refuses the read.</exception>
    /// <exception cref="InvalidOperationException">The read returned no row. A sequence always returns one value, so this is a fault.</exception>
    public static async Task<T> NextSequenceValueAsync<T>(this DbContext db, string sequenceName, CancellationToken ct = default)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(db);
        var sql = "SELECT NEXT VALUE FOR " + SqlIdentifier.QuoteQualifiedName(sequenceName, nameof(sequenceName));

        var values = await db.Database.SqlQueryRaw<T>(sql).ToListAsync(ct);
        if (values.Count == 0)
        {
            throw new InvalidOperationException($"The sequence {sequenceName} returned no value.");
        }
        return values[0];
    }
}
