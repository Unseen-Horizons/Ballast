// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ballast.Health.Tests;

/// <summary>
/// The schema check against a real migration history. A database migrated only to its first migration is Unhealthy, a fully
/// migrated one is Healthy, and a history with an id this code does not ship is still Healthy. Each run uses a database of its
/// own, dropped at the end. Skipped when SQL Server cannot be reached (set <c>BALLAST_TEST_SQL</c> to run it).
/// </summary>
public class SchemaHealthCheckSqlTests
{
    private static MigrationProbeContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<MigrationProbeContext>().UseSqlServer(connectionString).Options);

    private static SchemaHealthCheck CheckFor(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<MigrationProbeContext>(options => options.UseSqlServer(connectionString));
        var provider = services.BuildServiceProvider();
        var source = new EfMigrationSource<MigrationProbeContext>(provider.GetRequiredService<IServiceScopeFactory>());
        return new SchemaHealthCheck(source, Options.Create(new HealthOptions()), TimeProvider.System, NullLogger<SchemaHealthCheck>.Instance);
    }

    private static async Task<HealthStatus> StatusAsync(string connectionString) =>
        (await CheckFor(connectionString).CheckHealthAsync(new HealthCheckContext())).Status;

    private static async Task DropAsync(string connectionString)
    {
        await using var db = NewContext(connectionString);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task A_database_migrated_only_to_its_first_migration_is_unhealthy()
    {
        var reason = await SqlTestDatabase.UnreachableReasonAsync();
        if (reason is not null)
        {
            Assert.Skip($"SQL Server not reachable: {reason}");
        }

        var connection = SqlTestDatabase.ConnectionStringFor(SqlTestDatabase.NewName());
        try
        {
            await using (var db = NewContext(connection))
            {
                var first = db.Database.GetMigrations().First();
                await db.Database.MigrateAsync(first);
            }

            Assert.Equal(HealthStatus.Unhealthy, await StatusAsync(connection));
        }
        finally
        {
            await DropAsync(connection);
        }
    }

    [Fact]
    public async Task A_fully_migrated_database_is_healthy()
    {
        var reason = await SqlTestDatabase.UnreachableReasonAsync();
        if (reason is not null)
        {
            Assert.Skip($"SQL Server not reachable: {reason}");
        }

        var connection = SqlTestDatabase.ConnectionStringFor(SqlTestDatabase.NewName());
        try
        {
            await using (var db = NewContext(connection))
            {
                await db.Database.MigrateAsync();
            }

            Assert.Equal(HealthStatus.Healthy, await StatusAsync(connection));
        }
        finally
        {
            await DropAsync(connection);
        }
    }

    [Fact]
    public async Task A_history_with_an_id_this_code_does_not_ship_is_still_healthy()
    {
        var reason = await SqlTestDatabase.UnreachableReasonAsync();
        if (reason is not null)
        {
            Assert.Skip($"SQL Server not reachable: {reason}");
        }

        var connection = SqlTestDatabase.ConnectionStringFor(SqlTestDatabase.NewName());
        try
        {
            await using (var db = NewContext(connection))
            {
                await db.Database.MigrateAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({"20991231000000_FromANewerBuild"}, {"10.0.0"})");
            }

            Assert.Equal(HealthStatus.Healthy, await StatusAsync(connection));
        }
        finally
        {
            await DropAsync(connection);
        }
    }
}
