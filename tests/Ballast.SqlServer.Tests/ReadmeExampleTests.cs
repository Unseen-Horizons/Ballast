// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.SqlServer;

using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer.Tests.ReadmeExample;

/// <summary>
/// Runs the example in src/Ballast.SqlServer/README.md as it is written there. The types of the example are at the top of this
/// file, because a test method cannot declare them; the statements are the method's body. The namespace is its own, because the
/// example's <c>Order</c> is not the test model's.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.SqlServer/README.md.
        // The connection string the app reads at startup. Outside Development, a string for this computer is refused.
        var connectionString = "Server=localhost;Database=Shop;Trusted_Connection=True";
        Console.WriteLine(ConnectionStringCheck.Classify(connectionString));

        // The model the context builds: the enum is a text column of up to 32 characters.
        await using var db = new ShopContext(new DbContextOptionsBuilder<ShopContext>().UseSqlServer("Server=.;Database=Shop").Options);
        var status = db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Status))!;
        Console.WriteLine(status.GetColumnType());

        // A save that hit a unique index is a race, not a fault. SqlErrors looks through the DbUpdateException to the SQL error.
        var duplicate = new DbUpdateException("save failed", new InvalidOperationException("no SQL error here"));
        Console.WriteLine(SqlErrors.IsUniqueViolation(duplicate));

        // A script with GO lines, split into the batches SQL Server runs one at a time.
        Console.WriteLine(SqlBatches.Split("SELECT 1\nGO\nSELECT 2").Count);

        // The values the example prints.
        Assert.Equal(ConnectionStringProblem.LocalHost, ConnectionStringCheck.Classify(connectionString));
        Assert.Equal("nvarchar(32)", status.GetColumnType());
        Assert.False(SqlErrors.IsUniqueViolation(duplicate));
        Assert.Equal(2, SqlBatches.Split("SELECT 1\nGO\nSELECT 2").Count);
    }
}

// The example's types, copied verbatim from src/Ballast.SqlServer/README.md.

// A context that stores enums as text of up to 32 characters, and restricts deletes, as both APIs do.
public class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseEnumsAsStrings(maxLength: 32);
        modelBuilder.RestrictDeletes();
    }
}

public enum OrderStatus { Pending, Paid }

public class Order
{
    public int Id { get; set; }
    public OrderStatus Status { get; set; }
}
