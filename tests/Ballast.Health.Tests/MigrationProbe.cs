// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ballast.Health.Tests;

/// <summary>
/// A DbContext with no entities and two small migrations, so that the schema check can be tested against a real migration history
/// without any application's model.
/// </summary>
public sealed class MigrationProbeContext : DbContext
{
    /// <summary>Creates the context over its options.</summary>
    /// <param name="options">The options, with the SQL Server provider.</param>
    public MigrationProbeContext(DbContextOptions<MigrationProbeContext> options)
        : base(options)
    {
    }
}

/// <summary>The first probe migration: creates the probe table.</summary>
[DbContext(typeof(MigrationProbeContext))]
[Migration("20260101000000_CreateProbe")]
public sealed class CreateProbe : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE [Probe] ([Id] int NOT NULL CONSTRAINT [PK_Probe] PRIMARY KEY);");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE [Probe];");
}

/// <summary>The second probe migration: adds a column to the probe table.</summary>
[DbContext(typeof(MigrationProbeContext))]
[Migration("20260102000000_AddProbeNote")]
public sealed class AddProbeNote : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE [Probe] ADD [Note] nvarchar(50) NULL;");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE [Probe] DROP COLUMN [Note];");
}
