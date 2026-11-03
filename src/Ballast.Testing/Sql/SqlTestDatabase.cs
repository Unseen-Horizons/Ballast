// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace Ballast.Testing.Sql;

/// <summary>
/// A database for one test, named by <see cref="TestDatabase.NewName"/> and dropped when the test ends. When the server cannot be
/// reached, the calling test is skipped with <c>Assert.Skip</c>, so it shows as skipped and never as passed.
/// </summary>
public sealed class SqlTestDatabase : IAsyncDisposable
{
    private SqlTestDatabase(string name, string connectionString)
    {
        Name = name;
        ConnectionString = connectionString;
    }

    /// <summary>The database's name, with the run's marker and run id in it.</summary>
    public string Name { get; }

    /// <summary>A connection string for the database.</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Creates the database, or skips the calling test when the server cannot be reached. The skip message begins
    /// <c>SQL Server not reachable:</c>.
    /// </summary>
    /// <param name="purpose">What the database is for: letters and digits, starting with a letter, at most 64 characters.</param>
    /// <param name="collation">The database's collation. Null uses the server's default.</param>
    /// <param name="ct">Cancels the creation.</param>
    public static async Task<SqlTestDatabase> CreateAsync(string purpose, string? collation = null, CancellationToken ct = default)
    {
        var reason = await TestDatabase.UnreachableReasonAsync(ct);
        if (reason is not null)
            Assert.Skip($"SQL Server not reachable: {reason}");

        var name = TestDatabase.NewName(purpose);
        await TestDatabase.CreateEmptyAsync(name, collation, ct);
        return new SqlTestDatabase(name, TestDatabase.ConnectionStringFor(name));
    }

    /// <summary>Drops the database. Pooled connections to it are closed first, so the drop does not wait on them.</summary>
    public async ValueTask DisposeAsync() => await TestDatabase.DropAsync(Name);
}
