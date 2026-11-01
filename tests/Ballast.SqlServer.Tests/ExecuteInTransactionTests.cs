// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Data;

using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer.Tests;

/// <summary>The transaction helper against a real server: retries, the reset of tracked state, joining, and the isolation level.</summary>
public class ExecuteInTransactionTests
{
    [Fact]
    public async Task A_retry_after_a_transient_failure_resets_the_tracked_state_and_saves_the_row_once()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();

        await using var db = SampleContext.Create(database.ConnectionString, retrying: true);
        var attempts = 0;
        List<int> trackedAtStart = [];

        await db.ExecuteInTransactionAsync(async ct =>
        {
            attempts++;
            trackedAtStart.Add(db.ChangeTracker.Entries().Count());

            db.Items.Add(new Item { Name = "saved" });
            await db.SaveChangesAsync(ct);          // inserted inside the transaction
            db.Items.Add(new Item { Name = "pending" }); // added, never saved

            if (attempts == 1)
            {
                throw SqlExceptions.Create(SqlErrors.DeadlockVictim); // transient: the strategy replays the work
            }
        });

        Assert.Equal(2, attempts);
        int[] expectedTracked = [0, 0];
        Assert.Equal(expectedTracked, trackedAtStart);

        await using var check = SampleContext.Create(database.ConnectionString);
        var names = await check.Items.Select(i => i.Name).ToListAsync();
        string[] expectedNames = ["saved"];
        Assert.Equal(expectedNames, names);
    }

    [Fact]
    public async Task The_work_of_a_successful_attempt_commits_and_its_result_is_returned()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();

        await using var db = SampleContext.Create(database.ConnectionString);
        var id = await db.ExecuteInTransactionAsync(async ct =>
        {
            var item = new Item { Name = "committed" };
            db.Items.Add(item);
            await db.SaveChangesAsync(ct);
            return item.Id;
        });

        await using var check = SampleContext.Create(database.ConnectionString);
        Assert.Equal(id, (await check.Items.SingleAsync()).Id);
    }

    [Fact]
    public async Task A_call_without_a_result_commits_its_work()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();

        await using var db = SampleContext.Create(database.ConnectionString);
        await db.ExecuteInTransactionAsync(async ct =>
        {
            db.Items.Add(new Item { Name = "no result" });
            await db.SaveChangesAsync(ct);
        });

        await using var check = SampleContext.Create(database.ConnectionString);
        Assert.Equal("no result", (await check.Items.SingleAsync()).Name);
    }

    [Fact]
    public async Task Joining_an_open_transaction_runs_the_work_inside_it_and_leaves_the_commit_to_its_owner()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();

        await using var db = SampleContext.Create(database.ConnectionString);
        await using var outer = await db.Database.BeginTransactionAsync();
        var seenOuter = false;

        await db.ExecuteInTransactionAsync(async ct =>
        {
            seenOuter = ReferenceEquals(db.Database.CurrentTransaction, outer);
            db.Items.Add(new Item { Name = "joined" });
            await db.SaveChangesAsync(ct);
        });

        Assert.True(seenOuter, "the work runs in the transaction that was already open");
        await outer.RollbackAsync();

        await using var check = SampleContext.Create(database.ConnectionString);
        Assert.Empty(await check.Items.ToListAsync());
    }

    [Fact]
    public async Task The_isolation_level_is_applied_to_a_transaction_the_call_opens()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = SampleContext.Create(database.ConnectionString);

        var level = await db.ExecuteInTransactionAsync(async ct => await ReadIsolationLevelAsync(db, ct), IsolationLevel.Serializable);

        Assert.Equal(4, level); // SQL Server's transaction_isolation_level: 4 is SERIALIZABLE
    }

    [Fact]
    public async Task A_failure_that_is_not_transient_is_not_retried_and_rolls_the_work_back()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();

        await using var db = SampleContext.Create(database.ConnectionString, retrying: true);
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecuteInTransactionAsync(async ct =>
        {
            attempts++;
            db.Items.Add(new Item { Name = "rolled back" });
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("refused by the work");
        }));

        Assert.Equal(1, attempts);
        await using var check = SampleContext.Create(database.ConnectionString);
        Assert.Empty(await check.Items.ToListAsync());
    }

    [Fact]
    public async Task When_the_retries_run_out_the_last_transient_failure_is_still_found_by_the_classifiers()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();

        await using var db = SampleContext.Create(database.ConnectionString, retrying: true);
        var attempts = 0;

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteInTransactionAsync(async ct =>
        {
            attempts++;
            await Task.Yield();
            throw SqlExceptions.Create(SqlErrors.DeadlockVictim);
        }));

        Assert.True(attempts > 1, "the strategy retried the transient failure");
        Assert.True(SqlErrors.IsDeadlock(thrown));
    }

    [Fact]
    public async Task Null_arguments_are_refused_before_any_work_runs()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = SampleContext.Create(database.ConnectionString);

        await Assert.ThrowsAsync<ArgumentNullException>(() => db.ExecuteInTransactionAsync((Func<CancellationToken, Task>)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ((DbContext)null!).ExecuteInTransactionAsync(async ct => { await Task.CompletedTask; }));
    }

    private static async Task<int> ReadIsolationLevelAsync(DbContext db, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<short>(
            "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID").ToListAsync(ct);
        return rows[0];
    }
}
