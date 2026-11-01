// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Data.SqlClient;

namespace Ballast.Health.Tests;

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

    /// <summary>A new database name: <c>Ballast_Health_</c> and eight hex digits.</summary>
    public static string NewName() => "Ballast_Health_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>The connection string to one database on the server.</summary>
    /// <param name="database">The database name.</param>
    public static string ConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder(Server) { InitialCatalog = database }.ConnectionString;

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
            return $"{ex.GetType().Name}: {ex.Message}. Set {ConnectionVariable} to run this test.";
        }
    }
}
