// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Data.SqlClient;

namespace Ballast.Auth.Tests;

/// <summary>
/// The SQL Server that the tests use. The environment variable <c>BALLAST_TEST_SQL</c> names the server (a connection string without a
/// database); the default is the local server with Windows authentication. Each test makes a database of its own, with a unique name,
/// and drops it at the end, so several runs can share one server.
/// </summary>
internal sealed class SqlTestDatabase : IAsyncDisposable
{
    /// <summary>The environment variable that names the server.</summary>
    public const string ConnectionVariable = "BALLAST_TEST_SQL";

    /// <summary>The server the tests use when the variable is not set.</summary>
    public const string DefaultConnection = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly string _name;
    private readonly string _server;

    private SqlTestDatabase(string server, string name, string connectionString)
    {
        _server = server;
        _name = name;
        ConnectionString = connectionString;
    }

    /// <summary>The connection string to this database. Pooling is off, so closing a connection ends its session.</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Creates a database, or skips the calling test with <c>Assert.Skip</c> when the server cannot be reached, so the test shows as skipped
    /// and never as passed.
    /// </summary>
    /// <returns>The database, which is dropped when it is disposed.</returns>
    public static async Task<SqlTestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
        var server = string.IsNullOrWhiteSpace(configured) ? DefaultConnection : configured;
        var master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false, ConnectTimeout = 5 }.ConnectionString;

        try
        {
            await using var probe = new SqlConnection(master);
            await probe.OpenAsync();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            Assert.Skip($"SQL Server not reachable: {ex.Message}. Set {ConnectionVariable} to run this test.");
        }

        var name = "Ballast_Auth_" + Guid.NewGuid().ToString("N")[..8];
        await using (var connection = new SqlConnection(master))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]";
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name, Pooling = false }.ConnectionString;
        return new SqlTestDatabase(server, name, connectionString);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(_server) { InitialCatalog = "master", Pooling = false }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{_name}') IS NOT NULL BEGIN ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]; END";
        await command.ExecuteNonQueryAsync();
    }
}
