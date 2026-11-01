// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer.Tests;

/// <summary>A row with a status, for the claim tests.</summary>
public sealed class Order
{
    public int Id { get; set; }

    public string Status { get; set; } = string.Empty;
}

/// <summary>A row with a unique name, for the save tests.</summary>
public sealed class Item
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>The context the SQL tests use. Its tables are created from the model with <c>EnsureCreatedAsync</c>.</summary>
public sealed class SampleContext : DbContext
{
    public SampleContext(DbContextOptions<SampleContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The tests give the order ids themselves, so they are not identity columns.
        modelBuilder.Entity<Order>(entity => entity.Property(o => o.Id).ValueGeneratedNever());
        modelBuilder.Entity<Item>(entity =>
        {
            entity.Property(i => i.Name).HasMaxLength(64);
            entity.HasIndex(i => i.Name).IsUnique();
        });
    }

    /// <summary>A context for a connection string. With <paramref name="retrying"/>, transient faults are retried three times, 10 ms apart at most.</summary>
    public static SampleContext Create(string connectionString, bool retrying = false, bool withResetInterceptor = false)
    {
        var builder = new DbContextOptionsBuilder<SampleContext>();
        builder.UseSqlServer(connectionString, sql =>
        {
            if (retrying)
            {
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(10), null);
            }
        });
        if (withResetInterceptor)
        {
            builder.AddInterceptors(IsolationLevelResetInterceptor.Instance);
        }
        return new SampleContext(builder.Options);
    }
}
