// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer.Tests;

/// <summary>The claim helper against a real server: one winner per row, no overwrite, and the rule for more than one row.</summary>
public class ClaimAsyncTests
{
    [Fact]
    public async Task Two_claims_of_one_row_only_the_first_wins()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();
        await SeedOrdersAsync(database, (1, "Open"));

        await using var first = SampleContext.Create(database.ConnectionString);
        await using var second = SampleContext.Create(database.ConnectionString);

        var firstWon = await first.Orders.Where(o => o.Id == 1 && o.Status == "Open")
            .ClaimAsync(s => s.SetProperty(o => o.Status, "Taken"));
        var secondWon = await second.Orders.Where(o => o.Id == 1 && o.Status == "Open")
            .ClaimAsync(s => s.SetProperty(o => o.Status, "Taken"));

        Assert.True(firstWon);
        Assert.False(secondWon);
        Assert.Equal("Taken", await ReadStatusAsync(database, 1));
    }

    [Fact]
    public async Task A_claim_on_a_row_in_another_state_returns_false_and_overwrites_nothing()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();
        await SeedOrdersAsync(database, (1, "Shipped"));

        await using var db = SampleContext.Create(database.ConnectionString);
        var won = await db.Orders.Where(o => o.Id == 1 && o.Status == "Open")
            .ClaimAsync(s => s.SetProperty(o => o.Status, "Taken"));

        Assert.False(won);
        Assert.Equal("Shipped", await ReadStatusAsync(database, 1));
    }

    [Fact]
    public async Task A_claim_on_a_missing_row_returns_false()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();

        await using var db = SampleContext.Create(database.ConnectionString);
        var won = await db.Orders.Where(o => o.Id == 99 && o.Status == "Open")
            .ClaimAsync(s => s.SetProperty(o => o.Status, "Taken"));

        Assert.False(won);
    }

    [Fact]
    public async Task A_claim_that_changes_more_than_one_row_throws_and_a_transaction_the_caller_opened_rolls_it_back()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var setup = SampleContext.Create(database.ConnectionString)) await setup.Database.EnsureCreatedAsync();
        await SeedOrdersAsync(database, (1, "Open"), (2, "Open"));

        await using var db = SampleContext.Create(database.ConnectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Orders.Where(o => o.Status == "Open")
            .ClaimAsync(s => s.SetProperty(o => o.Status, "Taken")));
        Assert.Contains("2 rows", thrown.Message, StringComparison.Ordinal);

        await transaction.RollbackAsync();
        Assert.Equal("Open", await ReadStatusAsync(database, 1));
        Assert.Equal("Open", await ReadStatusAsync(database, 2));
    }

    [Fact]
    public async Task Null_arguments_are_refused()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = SampleContext.Create(database.ConnectionString);

        await Assert.ThrowsAsync<ArgumentNullException>(() => ((IQueryable<Order>)null!).ClaimAsync(s => s.SetProperty(o => o.Status, "x")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Orders.ClaimAsync(null!));
    }

    private static async Task SeedOrdersAsync(SqlTestDatabase database, params (int Id, string Status)[] rows)
    {
        await using var db = SampleContext.Create(database.ConnectionString);
        foreach (var (id, status) in rows)
        {
            db.Orders.Add(new Order { Id = id, Status = status });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<string> ReadStatusAsync(SqlTestDatabase database, int id)
    {
        await using var db = SampleContext.Create(database.ConnectionString);
        return (await db.Orders.SingleAsync(o => o.Id == id)).Status;
    }
}
