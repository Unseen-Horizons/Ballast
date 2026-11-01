// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Data.SqlClient;

namespace Ballast.SqlServer.Tests;

/// <summary>
/// A uniquely named database for one test, created on the server in <c>BALLAST_TEST_SQL</c> (by default a trusted local
/// connection). It is created when the test starts and dropped when the test ends. When the server cannot be reached, the test is
/// skipped with <c>Assert.Skip</c>, so it shows as skipped and never as passed.
/// </summary>
internal sealed class SqlTestDatabase : IAsyncDisposable
{
    /// <summary>The environment variable that names the test server's connection string.</summary>
    public const string EnvironmentVariable = "BALLAST_TEST_SQL";

    /// <summary>The server used when the variable is not set.</summary>
    public const string DefaultServer = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly string _masterConnection;

    private SqlTestDatabase(string masterConnection, string name, string connectionString)
    {
        _masterConnection = masterConnection;
        Name = name;
        ConnectionString = connectionString;
    }

    /// <summary>The database's name: <c>Ballast_SqlServer_</c> and eight hex digits.</summary>
    public string Name { get; }

    /// <summary>A connection string for the database.</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Creates the database, or skips the calling test when the server cannot be reached.
    /// </summary>
    /// <param name="collation">The database's collation. Null uses the server's default.</param>
    /// <param name="ct">Cancels the creation.</param>
    public static async Task<SqlTestDatabase> CreateAsync(string? collation = null, CancellationToken ct = default)
    {
        var server = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(server))
        {
            server = DefaultServer;
        }

        // Pooling off: the administration connections close at once, so the database can be dropped.
        var master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false }.ConnectionString;

        try
        {
            await using var probe = new SqlConnection(master);
            await probe.OpenAsync(ct);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            Assert.Skip($"SQL Server not reachable: {ex.Message}");
        }

        var name = "Ballast_SqlServer_" + Guid.NewGuid().ToString("N")[..8];
        await using (var connection = new SqlConnection(master))
        {
            await connection.OpenAsync(ct);
            var collate = string.IsNullOrWhiteSpace(collation) ? string.Empty : " COLLATE " + collation;
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]{collate}";
            await command.ExecuteNonQueryAsync(ct);
        }

        var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        return new SqlTestDatabase(master, name, connectionString);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Pooled connections to the database would keep it in use; close them before the drop.
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(_masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END";
        await command.ExecuteNonQueryAsync();
    }
}
