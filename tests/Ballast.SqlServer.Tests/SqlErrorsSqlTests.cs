// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer.Tests;

/// <summary>The classifiers on errors that a real SQL Server raises, through EF Core, as an app meets them.</summary>
public class SqlErrorsSqlTests
{
    [Fact]
    public async Task A_real_duplicate_on_a_unique_index_is_a_unique_violation()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = SampleContext.Create(database.ConnectionString);
        await db.Database.EnsureCreatedAsync();
        db.Items.Add(new Item { Name = "taken" });
        await db.SaveChangesAsync();

        await using var other = SampleContext.Create(database.ConnectionString);
        other.Items.Add(new Item { Name = "taken" });
        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());

        Assert.True(SqlErrors.IsUniqueViolation(thrown));
        Assert.False(SqlErrors.IsDeadlock(thrown));
        Assert.Contains(SqlErrors.ErrorNumbers(thrown), n => n is SqlErrors.DuplicateKey or SqlErrors.UniqueConstraint);
    }

    [Fact]
    public async Task A_save_that_waits_on_a_held_row_until_the_command_timeout_is_busy_and_transient()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var setup = SampleContext.Create(database.ConnectionString);
        await setup.Database.EnsureCreatedAsync();
        setup.Items.Add(new Item { Name = "held" });
        await setup.SaveChangesAsync();
        var itemId = (await setup.Items.SingleAsync(i => i.Name == "held")).Id;

        // A second connection takes an update lock on the row and keeps it until the test rolls back.
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var hold = holder.CreateCommand())
        {
            hold.Transaction = (SqlTransaction)transaction;
            hold.CommandText = "SELECT Id FROM Items WITH (UPDLOCK, ROWLOCK) WHERE Id = @id";
            hold.Parameters.Add(new SqlParameter("@id", itemId));
            await hold.ExecuteScalarAsync();
        }

        await using var victim = SampleContext.Create(database.ConnectionString);
        victim.Database.SetCommandTimeout(1);
        var row = await victim.Items.SingleAsync(i => i.Id == itemId);
        row.Name = "changed";
        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => victim.SaveChangesAsync());

        Assert.Contains(SqlErrors.ErrorNumbers(thrown), n => n == SqlErrors.CommandTimeout);
        Assert.True(SqlErrors.IsBusy(thrown));
        Assert.True(SqlErrors.IsTransient(thrown));
        Assert.False(SqlErrors.IsDeadlock(thrown));
        Assert.False(SqlErrors.IsUniqueViolation(thrown));

        await transaction.RollbackAsync();
    }
}
