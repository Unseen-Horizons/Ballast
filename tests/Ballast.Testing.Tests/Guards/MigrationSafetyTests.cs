// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Guards.MigrationSafety;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ballast.Testing.Tests.Guards.MigrationSafety;

/// <summary>The context and migrations the discovery test reads. Only their <c>Up</c> operations are judged; nothing runs.</summary>
public sealed class GuardSampleContext(DbContextOptions<GuardSampleContext> options) : DbContext(options)
{
}

[DbContext(typeof(GuardSampleContext))]
[Migration("20260201000000_DropsLegacyColumn")]
public sealed class DropsLegacyColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Legacy", table: "Trips");

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(GuardSampleContext))]
[Migration("20260101000000_Additive")]
public sealed class Additive : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Note", table: "Trips", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

public class MigrationSafetyTests
{
    private const string Reason = "Reviewed: the old copy never reads this column, and the removal waits for the switch.";
    private const string Cutoff = "20260101000000_Additive";
    private const string Future = "29990101000000_Synthetic";

    [Fact]
    public void A_dropped_column_is_a_drop_column_violation()
    {
        var violation = Assert.Single(MigrationSafetyRules.Evaluate([new DropColumnOperation { Table = "Trips", Name = "Old" }]));

        Assert.Equal("DropColumn", violation.Rule);
        Assert.Equal("Trips.Old", violation.Detail);
    }

    [Fact]
    public void A_dropped_table_renamed_table_and_renamed_column_are_each_flagged()
    {
        var violations = MigrationSafetyRules.Evaluate(
        [
            new DropTableOperation { Name = "Old" },
            new RenameTableOperation { Name = "A", NewName = "B" },
            new RenameColumnOperation { Table = "T", Name = "C", NewName = "D" },
        ]);

        Assert.Equal(["DropTable", "RenameTable", "RenameColumn"], violations.Select(v => v.Rule));
    }

    [Fact]
    public void A_required_column_added_without_a_default_is_flagged_and_a_nullable_one_is_not()
    {
        var required = MigrationSafetyRules.Evaluate([new AddColumnOperation { Table = "T", Name = "C", IsNullable = false }]);
        var nullable = MigrationSafetyRules.Evaluate([new AddColumnOperation { Table = "T", Name = "C", IsNullable = true }]);
        var defaulted = MigrationSafetyRules.Evaluate([new AddColumnOperation { Table = "T", Name = "C", IsNullable = false, DefaultValue = 0 }]);

        Assert.Equal("AddColumnRequired", Assert.Single(required).Rule);
        Assert.Empty(nullable);
        Assert.Empty(defaulted);
    }

    [Fact]
    public void Making_a_column_required_lengthening_nothing_and_changing_its_type_are_each_flagged()
    {
        var becomesRequired = MigrationSafetyRules.Evaluate(
            [new AlterColumnOperation { Table = "T", Name = "C", IsNullable = false, OldColumn = new AddColumnOperation { IsNullable = true } }]);
        var narrower = MigrationSafetyRules.Evaluate(
            [new AlterColumnOperation { Table = "T", Name = "C", IsNullable = true, MaxLength = 50, OldColumn = new AddColumnOperation { IsNullable = true, MaxLength = 100 } }]);
        var unbounded = MigrationSafetyRules.Evaluate(
            [new AlterColumnOperation { Table = "T", Name = "C", IsNullable = true, MaxLength = 50, OldColumn = new AddColumnOperation { IsNullable = true } }]);
        var retyped = MigrationSafetyRules.Evaluate(
            [new AlterColumnOperation { Table = "T", Name = "C", IsNullable = true, ColumnType = "int", OldColumn = new AddColumnOperation { IsNullable = true, ColumnType = "nvarchar(10)" } }]);
        var widened = MigrationSafetyRules.Evaluate(
            [new AlterColumnOperation { Table = "T", Name = "C", IsNullable = true, MaxLength = 200, OldColumn = new AddColumnOperation { IsNullable = true, MaxLength = 100 } }]);

        Assert.Equal("AlterColumnRequired", Assert.Single(becomesRequired).Rule);
        Assert.Equal("AlterColumnLength", Assert.Single(narrower).Rule);
        Assert.Equal("AlterColumnLength", Assert.Single(unbounded).Rule);
        Assert.Equal("AlterColumnType", Assert.Single(retyped).Rule);
        Assert.Empty(widened);
    }

    [Fact]
    public void An_alter_without_its_previous_definition_is_flagged_rather_than_passed()
    {
        // EF sets a default previous definition, so the missing case is reached by clearing it.
        var violation = Assert.Single(MigrationSafetyRules.Evaluate([new AlterColumnOperation { Table = "T", Name = "C", OldColumn = null! }]));

        Assert.Equal("AlterColumn", violation.Rule);
    }

    [Fact]
    public void Raw_sql_that_drops_deletes_truncates_or_renames_is_flagged()
    {
        var drop = MigrationSafetyRules.Evaluate([new SqlOperation { Sql = "DROP TABLE Old;" }]);
        var delete = MigrationSafetyRules.Evaluate([new SqlOperation { Sql = "delete from Trips where Id = 1" }]);
        var truncate = MigrationSafetyRules.Evaluate([new SqlOperation { Sql = "TRUNCATE TABLE Trips" }]);
        var rename = MigrationSafetyRules.Evaluate([new SqlOperation { Sql = "EXEC sp_rename 'A', 'B';" }]);
        var notNull = MigrationSafetyRules.Evaluate([new SqlOperation { Sql = "ALTER TABLE T ALTER COLUMN C nvarchar(10) NOT NULL;" }]);

        Assert.All(new[] { drop, delete, truncate, rename, notNull }, violations => Assert.Equal("RawSql", Assert.Single(violations).Rule));
    }

    [Fact]
    public void On_delete_cascade_on_a_foreign_key_is_not_a_delete()
    {
        var violations = MigrationSafetyRules.Evaluate(
            [new SqlOperation { Sql = "ALTER TABLE T ADD CONSTRAINT FK_T_A FOREIGN KEY (A) REFERENCES A (Id) ON DELETE CASCADE;" }]);

        Assert.Empty(violations);
    }

    [Fact]
    public void The_rule_names_are_listed_for_the_allow_list_to_name()
    {
        Assert.Contains("DropColumn", MigrationSafetyRules.RuleNames);
        Assert.Contains("RawSql", MigrationSafetyRules.RuleNames);
    }

    [Fact]
    public void A_violation_fails_the_guard_with_the_exact_line_the_deploy_script_reads()
    {
        var migrations = new[] { new DiscoveredMigration(Future, [new MigrationViolation("DropColumn", "Trips.Old")]) };

        var failure = Assert.Single(MigrationGuard.Failures(migrations, Cutoff, []));

        Assert.Equal($"MIGRATION-GUARD {Future}: DropColumn: Trips.Old. {MigrationGuard.DefaultAdvice}", failure);
    }

    [Fact]
    public void A_violation_at_or_before_the_cutoff_is_grandfathered()
    {
        var migrations = new[] { new DiscoveredMigration(Cutoff, [new MigrationViolation("DropColumn", "Trips.Old")]) };

        Assert.Empty(MigrationGuard.Failures(migrations, Cutoff, []));
    }

    [Fact]
    public void A_valid_entry_excuses_exactly_the_rule_it_names()
    {
        var migrations = new[] { new DiscoveredMigration(Future, [new MigrationViolation("DropColumn", "Trips.Old")]) };
        var entry = new MigrationAllowEntry(Future, ["DropColumn"], Reason);

        Assert.Empty(MigrationGuard.Failures(migrations, Cutoff, [entry]));
    }

    [Fact]
    public void An_entry_that_excuses_one_rule_leaves_another_rule_of_the_same_migration_flagged()
    {
        var migrations = new[]
        {
            new DiscoveredMigration(Future, [new MigrationViolation("DropColumn", "Trips.Old"), new MigrationViolation("RawSql", "DELETE")]),
        };
        var entry = new MigrationAllowEntry(Future, ["DropColumn"], Reason);

        var failure = Assert.Single(MigrationGuard.Failures(migrations, Cutoff, [entry]));
        Assert.StartsWith($"MIGRATION-GUARD {Future}: RawSql: DELETE.", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_for_a_missing_migration_fails_and_excuses_nothing()
    {
        var migrations = new[] { new DiscoveredMigration(Future, [new MigrationViolation("DropColumn", "Trips.Old")]) };
        var entry = new MigrationAllowEntry("29990101000001_Missing", ["DropColumn"], Reason);

        var failures = MigrationGuard.Failures(migrations, Cutoff, [entry]);

        Assert.Equal("MIGRATION-ALLOW-LIST 29990101000001_Missing: no such migration. Remove the entry or correct its id.", failures[0]);
        Assert.StartsWith("MIGRATION-GUARD", failures[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_for_a_grandfathered_migration_fails_because_it_excuses_nothing()
    {
        var migrations = new[] { new DiscoveredMigration(Cutoff, []) };
        var entry = new MigrationAllowEntry(Cutoff, ["DropColumn"], Reason);

        Assert.Equal($"MIGRATION-ALLOW-LIST {Cutoff}: grandfathered, so the entry excuses nothing. Remove it.", Assert.Single(MigrationGuard.Failures(migrations, Cutoff, [entry])));
    }

    [Fact]
    public void An_entry_whose_reason_is_too_short_fails()
    {
        var migrations = new[] { new DiscoveredMigration(Future, []) };
        var entry = new MigrationAllowEntry(Future, ["DropColumn"], "short");

        var failures = MigrationGuard.Failures(migrations, Cutoff, [entry]);

        Assert.Equal($"MIGRATION-ALLOW-LIST {Future}: the reason must be at least 20 characters.", failures[0]);
        Assert.Equal($"MIGRATION-ALLOW-LIST {Future}: stale, the migration has no DropColumn violation to excuse. Remove the rule.", failures[1]);
    }

    [Fact]
    public void An_entry_that_names_no_rules_fails()
    {
        var migrations = new[] { new DiscoveredMigration(Future, []) };
        var entry = new MigrationAllowEntry(Future, [], Reason);

        Assert.Equal($"MIGRATION-ALLOW-LIST {Future}: names no rules.", Assert.Single(MigrationGuard.Failures(migrations, Cutoff, [entry])));
    }

    [Fact]
    public void A_stale_rule_in_an_entry_fails_even_when_the_migration_has_other_violations()
    {
        var migrations = new[] { new DiscoveredMigration(Future, [new MigrationViolation("DropColumn", "Trips.Old")]) };
        var entry = new MigrationAllowEntry(Future, ["DropColumn", "DropTable"], Reason);

        // The entry has a stale rule, so it is invalid and excuses nothing: the DropColumn violation fails as well.
        var failures = MigrationGuard.Failures(migrations, Cutoff, [entry]);

        Assert.Equal(2, failures.Count);
        Assert.Equal($"MIGRATION-ALLOW-LIST {Future}: stale, the migration has no DropTable violation to excuse. Remove the rule.", failures[0]);
        Assert.StartsWith($"MIGRATION-GUARD {Future}: DropColumn:", failures[1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_advice_can_be_replaced_by_the_app_with_its_own_wording()
    {
        var migrations = new[] { new DiscoveredMigration(Future, [new MigrationViolation("DropColumn", "Trips.Old")]) };

        var failure = Assert.Single(MigrationGuard.Failures(migrations, Cutoff, [], advice: "Ask the database owner."));

        Assert.EndsWith(" Ask the database owner.", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Discovery_reads_each_migration_of_the_context_and_judges_its_up_operations()
    {
        var found = MigrationGuard.Discover(typeof(GuardSampleContext));

        Assert.Equal(["20260101000000_Additive", "20260201000000_DropsLegacyColumn"], found.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Empty(found.Single(m => m.Id == Cutoff).Violations);
        Assert.Equal("DropColumn", Assert.Single(found.Single(m => m.Id.EndsWith("DropsLegacyColumn", StringComparison.Ordinal)).Violations).Rule);
    }

    [Fact]
    public void The_markers_are_the_words_the_deploy_script_matches()
    {
        Assert.Equal("MIGRATION-GUARD", MigrationGuard.GuardMarker);
        Assert.Equal("MIGRATION-ALLOW-LIST", MigrationGuard.AllowListMarker);
    }
}
