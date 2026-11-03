// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Ballast.Testing.Guards.MigrationSafety;

/// <summary>One rule that a migration breaks. <see cref="Rule"/> names the rule; <see cref="Detail"/> says what in the migration broke it.</summary>
/// <param name="Rule">The rule's name, such as <c>DropColumn</c>. An allow-list entry names rules by this name.</param>
/// <param name="Detail">What in the migration broke the rule.</param>
public sealed record MigrationViolation(string Rule, string Detail);

/// <summary>
/// The migration-safety rules of a zero-downtime release. The copy of the code that is still serving reads and writes the schema as
/// it was before a migration, so a migration that removes, renames or tightens something that copy uses breaks it while it runs.
/// These rules flag those operations. Only <c>Up</c> is judged: a rollback never runs <c>Down</c>.
/// </summary>
/// <remarks>
/// A tripwire, not a proof. It reads the operations that <c>Up</c> builds, and a pattern over raw SQL can miss dynamic SQL or flag a
/// comment. The way past a flagged migration is to make it additive, or a reviewed allow-list entry; the rules themselves do not bend.
/// Raw data-changing SQL is judged only by its keywords. A rehearsal on a copy of the database is the control for raw SQL.
/// </remarks>
public static class MigrationSafetyRules
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private const int SnippetLength = 120;

    private static readonly Regex DestructiveKeyword = new(@"\b(?:DELETE|TRUNCATE|DROP)\b", Options);

    // "ON DELETE CASCADE" on a foreign key is not a delete. It is removed before the keyword search.
    private static readonly Regex OnDelete = new(@"\bON\s+DELETE\b", Options);

    private static readonly Regex SpRename = new(@"\bsp_rename\b", Options);

    private static readonly Regex AlterColumnNotNull = new(@"\bALTER\s+COLUMN\b[^;]*?\bNOT\s+NULL\b", Options);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The rule names this engine can report, in the order a reader meets them.</summary>
    public static readonly IReadOnlyList<string> RuleNames =
    [
        "DropTable", "DropColumn", "RenameTable", "RenameColumn", "DeleteData", "AlterColumn", "AlterColumnRequired",
        "AlterColumnLength", "AlterColumnType", "AddColumnRequired", "RawSql",
    ];

    /// <summary>Every violation in the operations, in the order they are built. Empty when the migration passes.</summary>
    /// <param name="operations">The operations of a migration's <c>Up</c>. Required.</param>
    public static IReadOnlyList<MigrationViolation> Evaluate(IEnumerable<MigrationOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        List<MigrationViolation> violations = [];
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case DropTableOperation dropTable:
                    violations.Add(new MigrationViolation("DropTable", Qualified(dropTable.Schema, dropTable.Name)));
                    break;

                case DropColumnOperation dropColumn:
                    violations.Add(new MigrationViolation("DropColumn", $"{Qualified(dropColumn.Schema, dropColumn.Table)}.{dropColumn.Name}"));
                    break;

                case RenameTableOperation renameTable:
                    violations.Add(new MigrationViolation("RenameTable", $"{Qualified(renameTable.Schema, renameTable.Name)} -> {renameTable.NewName}"));
                    break;

                case RenameColumnOperation renameColumn:
                    violations.Add(new MigrationViolation("RenameColumn", $"{Qualified(renameColumn.Schema, renameColumn.Table)}.{renameColumn.Name} -> {renameColumn.NewName}"));
                    break;

                case DeleteDataOperation deleteData:
                    violations.Add(new MigrationViolation("DeleteData", Qualified(deleteData.Schema, deleteData.Table)));
                    break;

                case AlterColumnOperation alterColumn:
                    AddAlterColumnViolations(alterColumn, violations);
                    break;

                case AddColumnOperation addColumn when IsRequiredWithoutDefault(addColumn):
                    violations.Add(new MigrationViolation("AddColumnRequired", $"{Qualified(addColumn.Schema, addColumn.Table)}.{addColumn.Name} is NOT NULL with no default"));
                    break;

                case SqlOperation sql:
                    AddSqlViolations(sql.Sql, violations);
                    break;
            }
        }

        return violations;
    }

    private static bool IsRequiredWithoutDefault(AddColumnOperation addColumn) =>
        !addColumn.IsNullable && addColumn.DefaultValue is null && addColumn.DefaultValueSql is null && addColumn.ComputedColumnSql is null;

    private static void AddAlterColumnViolations(AlterColumnOperation alterColumn, List<MigrationViolation> violations)
    {
        var column = $"{Qualified(alterColumn.Schema, alterColumn.Table)}.{alterColumn.Name}";
        var old = alterColumn.OldColumn;
        if (old is null)
        {
            // The migration builder always sets the previous definition, so this is a bug to surface, not a pass.
            violations.Add(new MigrationViolation("AlterColumn", $"{column}: no previous definition, so the change cannot be judged"));
            return;
        }

        if (!alterColumn.IsNullable && old.IsNullable)
        {
            violations.Add(new MigrationViolation("AlterColumnRequired", $"{column} was nullable and becomes NOT NULL"));
        }

        if (Narrows(old.MaxLength, alterColumn.MaxLength))
        {
            violations.Add(new MigrationViolation("AlterColumnLength", $"{column} MaxLength {Describe(old.MaxLength)} -> {Describe(alterColumn.MaxLength)}"));
        }

        if (!string.Equals(alterColumn.ColumnType, old.ColumnType, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new MigrationViolation("AlterColumnType", $"{column} store type {old.ColumnType ?? "(default)"} -> {alterColumn.ColumnType ?? "(default)"}"));
        }
    }

    // A length that is unbounded (null) now is never narrower. Going from unbounded to a bound is narrower.
    private static bool Narrows(int? oldLength, int? newLength) =>
        newLength is not null && (oldLength is null || newLength.Value < oldLength.Value);

    private static void AddSqlViolations(string sql, List<MigrationViolation> violations)
    {
        var snippet = Snippet(sql);

        var keywords = DestructiveKeyword.Matches(OnDelete.Replace(sql, " "))
            .Select(match => match.Value.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keywords.Count > 0)
        {
            violations.Add(new MigrationViolation("RawSql", $"{string.Join(", ", keywords)} in raw SQL: {snippet}"));
        }

        if (SpRename.IsMatch(sql))
        {
            violations.Add(new MigrationViolation("RawSql", $"sp_rename in raw SQL: {snippet}"));
        }

        if (AlterColumnNotNull.IsMatch(sql))
        {
            violations.Add(new MigrationViolation("RawSql", $"ALTER COLUMN ... NOT NULL in raw SQL: {snippet}"));
        }
    }

    private static string Describe(int? length) =>
        length is int value ? value.ToString(CultureInfo.InvariantCulture) : "(unbounded)";

    private static string Qualified(string? schema, string name) =>
        string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";

    private static string Snippet(string sql)
    {
        var flat = Whitespace.Replace(sql, " ").Trim();
        return flat.Length <= SnippetLength ? flat : flat[..SnippetLength] + "...";
    }
}
