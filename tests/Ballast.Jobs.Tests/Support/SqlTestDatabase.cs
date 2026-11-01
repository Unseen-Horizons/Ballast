// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Data.SqlClient;

namespace Ballast.Jobs.Tests;

/// <summary>
/// The SQL Server that the tests use. The environment variable <c>BALLAST_TEST_SQL</c> names the server (a connection string
/// without a database); the default is the local server with Windows authentication. Each test makes a database of its own, with
/// a unique name, and drops it at the end, so several runs can share one server.
/// </summary>
internal static class SqlTestDatabase
{
    /// <summary>The environment variable that names the server.</summary>
    public const string ConnectionVariable = "BALLAST_TEST_SQL";

    /// <summary>The server the tests use when the variable is not set.</summary>
    public const string DefaultConnection = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>The connection string to the server, without a database.</summary>
    public static string Server
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
            return string.IsNullOrWhiteSpace(configured) ? DefaultConnection : configured;
        }
    }

    /// <summary>A new database name: <c>Ballast_Jobs_</c> and eight hex digits.</summary>
    public static string NewName() => "Ballast_Jobs_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>The connection string to one database on the server. Pooling is off, so closing a connection ends its session.</summary>
    /// <param name="database">The database name.</param>
    public static string ConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder(Server) { InitialCatalog = database, Pooling = false }.ConnectionString;

    /// <summary>Tries to open the server, and says why it cannot.</summary>
    /// <returns>Null when the server answers, otherwise the reason, for <c>Assert.Skip</c>.</returns>
    public static async Task<string?> UnreachableReasonAsync()
    {
        try
        {
            var probe = new SqlConnectionStringBuilder(Server) { ConnectTimeout = 3, Pooling = false };
            await using var connection = new SqlConnection(probe.ConnectionString);
            await connection.OpenAsync();
            return null;
        }
        catch (Exception ex)
        {
            return $"SQL Server not reachable: {ex.GetType().Name}: {ex.Message}. Set {ConnectionVariable} to run this test.";
        }
    }
}

/// <summary>
/// A database made for one test and dropped when the test ends. Its lock is the database's own, as an app's lock is.
/// </summary>
internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _name;

    private TemporaryDatabase(string name, string connectionString)
    {
        _name = name;
        ConnectionString = connectionString;
    }

    /// <summary>The connection string to this database.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates the database. The caller must have checked that the server is reachable.</summary>
    /// <returns>The database, which is dropped when it is disposed.</returns>
    public static async Task<TemporaryDatabase> CreateAsync()
    {
        var name = SqlTestDatabase.NewName();
        await using var master = new SqlConnection(SqlTestDatabase.ConnectionStringFor("master"));
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}];";
        await command.ExecuteNonQueryAsync();
        return new TemporaryDatabase(name, SqlTestDatabase.ConnectionStringFor(name));
    }

    /// <summary>Drops the database. Sessions still open on it are ended first.</summary>
    public async ValueTask DisposeAsync()
    {
        await using var master = new SqlConnection(SqlTestDatabase.ConnectionStringFor("master"));
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];";
        await command.ExecuteNonQueryAsync();
    }
}
