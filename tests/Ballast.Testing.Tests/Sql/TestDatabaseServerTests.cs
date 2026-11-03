// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Fakes;
using Ballast.Testing.Sql;

using Microsoft.Data.SqlClient;

namespace Ballast.Testing.Tests.Sql;

/// <summary>
/// The database helpers against the SQL Server the tests run on. Each test makes its own database and drops it, and skips when the
/// server cannot be reached.
/// </summary>
public class TestDatabaseServerTests
{
    [Fact]
    public async Task A_database_is_created_for_the_test_and_dropped_when_the_test_ends()
    {
        await RequireServerAsync();

        string name;
        await using (var database = await SqlTestDatabase.CreateAsync("Probe"))
        {
            name = database.Name;
            Assert.True(await ExistsAsync(name));
            Assert.Equal(TestDatabase.ConnectionStringFor(name), database.ConnectionString);
        }

        Assert.False(await ExistsAsync(name));
    }

    [Fact]
    public async Task A_collation_is_applied_when_one_is_asked_for()
    {
        await RequireServerAsync();

        var name = TestDatabase.NewName("Collated");
        await TestDatabase.CreateEmptyAsync(name, "Latin1_General_CS_AS");
        try
        {
            Assert.Equal("Latin1_General_CS_AS", await CollationAsync(name));
        }
        finally
        {
            await TestDatabase.DropAsync(name);
        }
    }

    [Fact]
    public async Task Dropping_a_database_that_does_not_exist_leaves_the_server_alone()
    {
        await RequireServerAsync();

        var name = TestDatabase.NewName("Missing");
        await TestDatabase.DropAsync(name);
        Assert.False(await ExistsAsync(name));
    }

    [Fact]
    public async Task The_sweep_finds_a_database_that_another_run_left_behind_and_drops_it()
    {
        await RequireServerAsync();

        // A run id that is not this run's: the database is the sweep's to take, not this test's.
        var leftover = "Leftover" + TestDatabase.NameMarker + "fedcba987654_1";
        await CreateRawAsync(leftover);
        try
        {
            var stale = await TestDatabase.StaleNamesAsync(TestDatabase.RunId, TimeSpan.Zero);
            Assert.Contains(leftover, stale);

            await TestDatabase.DropAnyRunAsync(leftover);
            Assert.False(await ExistsAsync(leftover));
        }
        finally
        {
            await TestDatabase.DropAnyRunAsync(leftover);
        }
    }

    [Fact]
    public async Task The_sweep_never_lists_this_runs_own_databases_as_leftovers()
    {
        await RequireServerAsync();

        await using var database = await SqlTestDatabase.CreateAsync("Mine");
        var stale = await TestDatabase.StaleNamesAsync(TestDatabase.RunId, TimeSpan.Zero);
        Assert.DoesNotContain(database.Name, stale);
        Assert.Contains(database.Name, await TestDatabase.OwnNamesAsync());
    }

    [Fact]
    public async Task A_drop_of_any_run_refuses_a_database_that_is_not_marked()
    {
        await RequireServerAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => TestDatabase.DropAnyRunAsync("Ballast_Unmarked_Database"));
    }

    [Fact]
    public async Task The_server_probe_answers_null_for_a_reachable_server()
    {
        await RequireServerAsync();

        Assert.Null(await TestDatabase.UnreachableReasonAsync());
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_with_the_variable_to_set()
    {
        using var scope = EnvironmentVariableScope.Set("BALLAST_TEST_SQL", "Server=127.0.0.1,1;Connect Timeout=2;Trusted_Connection=True;TrustServerCertificate=True");
        var reason = await TestDatabase.UnreachableReasonAsync();

        // The variable is read on each call, so the probe sees the server set above.
        Assert.NotNull(reason);
        Assert.Contains("BALLAST_TEST_SQL", reason, StringComparison.Ordinal);
    }

    private static async Task RequireServerAsync()
    {
        var reason = await TestDatabase.UnreachableReasonAsync();
        if (reason is not null)
        {
            Assert.Skip($"SQL Server not reachable: {reason}");
        }
    }

    private static async Task<bool> ExistsAsync(string name)
    {
        await using var connection = new SqlConnection(TestDatabase.Master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name;";
        command.Parameters.AddWithValue("@name", name);
        return (int)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task<string?> CollationAsync(string name)
    {
        await using var connection = new SqlConnection(TestDatabase.Master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT collation_name FROM sys.databases WHERE name = @name;";
        command.Parameters.AddWithValue("@name", name);
        return (string?)await command.ExecuteScalarAsync();
    }

    // A database under a run name that is not this run's, made the way another run would have made it.
    private static async Task CreateRawAsync(string name)
    {
        await using var connection = new SqlConnection(TestDatabase.Master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}];";
        await command.ExecuteNonQueryAsync();
    }
}
