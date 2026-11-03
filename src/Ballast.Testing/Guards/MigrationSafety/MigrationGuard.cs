// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ballast.Testing.Guards.MigrationSafety;

/// <summary>A migration found in a context's assembly, with the violations its <c>Up</c> has under the rules.</summary>
/// <param name="Id">The migration's id, such as <c>20261020163450_JourneyRouteLegPathAndTrainNumber</c>.</param>
/// <param name="Violations">The rule violations of <c>Up</c>. Empty when the migration passes.</param>
public sealed record DiscoveredMigration(string Id, IReadOnlyList<MigrationViolation> Violations);

/// <summary>
/// A reviewed exception to the migration-safety rules: it excuses the named rules of one migration.
/// </summary>
/// <param name="MigrationId">The full migration id. An entry naming no such migration fails.</param>
/// <param name="Rules">The exact rule names it excuses. An entry naming a rule the migration does not break fails as stale.</param>
/// <param name="Reason">Why the exception is acceptable. At least <see cref="MigrationGuard.DefaultMinimumReasonLength"/> characters.</param>
public sealed record MigrationAllowEntry(string MigrationId, IReadOnlyList<string> Rules, string Reason);

/// <summary>
/// The migration-safety guard: every migration added after a cutoff must pass <see cref="MigrationSafetyRules"/>, or be excused by a
/// reviewed allow-list entry. A migration up to and including the cutoff is grandfathered: it ran against a database that was already
/// serving, so the rules are not applied to it.
/// </summary>
/// <remarks>
/// The failure lines are a contract. The deploy script (Ballast.Deploy) reads every line that starts with <c>MIGRATION-GUARD </c> or
/// <c>MIGRATION-ALLOW-LIST </c>, so their text and these two markers must not change. A guard test runs in CI, and its failure output
/// is what the deploy shows the owner.
/// </remarks>
public static class MigrationGuard
{
    /// <summary>The marker a violation's line starts with. The deploy script matches it.</summary>
    public const string GuardMarker = "MIGRATION-GUARD";

    /// <summary>The marker an allow-list problem's line starts with. The deploy script matches it.</summary>
    public const string AllowListMarker = "MIGRATION-ALLOW-LIST";

    /// <summary>The shortest reason an allow-list entry may give: 20 characters.</summary>
    public const int DefaultMinimumReasonLength = 20;

    /// <summary>The advice that ends every violation line. Override it with the app's own wording only with a reason.</summary>
    public const string DefaultAdvice = "Make it additive (expand now, contract later) or add a reviewed allow-list entry with a reason.";

    /// <summary>The provider name the migrations are read for. A migration's operations depend on the provider it is built for.</summary>
    public const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>
    /// Finds every migration of <paramref name="contextType"/> in its assembly, and judges each one's <c>Up</c>. The migration is built
    /// for <paramref name="provider"/>, because the provider decides which operations <c>Up</c> builds. Nothing is run against a database.
    /// </summary>
    /// <param name="contextType">The DbContext type. Its assembly is searched.</param>
    /// <param name="provider">The provider the operations are built for. Default SQL Server.</param>
    /// <exception cref="InvalidOperationException">A migration has no id or cannot be created.</exception>
    public static IReadOnlyList<DiscoveredMigration> Discover(Type contextType, string provider = SqlServerProvider)
    {
        ArgumentNullException.ThrowIfNull(contextType);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        List<DiscoveredMigration> found = [];
        foreach (var type in contextType.Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(Migration).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetCustomAttribute<DbContextAttribute>()?.ContextType != contextType)
            {
                continue;
            }

            var id = type.GetCustomAttribute<MigrationAttribute>()?.Id
                ?? throw new InvalidOperationException($"{type.FullName} has no MigrationAttribute.");
            var migration = Activator.CreateInstance(type) as Migration
                ?? throw new InvalidOperationException($"{type.FullName} cannot be created.");

            migration.ActiveProvider = provider;
            found.Add(new DiscoveredMigration(id, MigrationSafetyRules.Evaluate(migration.UpOperations)));
        }

        return found;
    }

    /// <summary>
    /// The guard's failure lines. A violation in a migration after the cutoff fails unless a valid entry names its rule. An entry that is
    /// not valid excuses nothing, so a broken entry fails twice: once for itself and once for the violation it was meant to cover. An
    /// empty result means the guard passes.
    /// </summary>
    /// <param name="migrations">The discovered migrations, from <see cref="Discover"/> or built in a test.</param>
    /// <param name="cutoffId">The newest grandfathered migration. Ids compare as ordinal text, so a timestamp prefix orders them.</param>
    /// <param name="allowList">The reviewed entries.</param>
    /// <param name="advice">The advice that ends each violation line. Default <see cref="DefaultAdvice"/>.</param>
    /// <param name="minimumReasonLength">The shortest reason an entry may give.</param>
    public static IReadOnlyList<string> Failures(
        IEnumerable<DiscoveredMigration> migrations,
        string cutoffId,
        IEnumerable<MigrationAllowEntry> allowList,
        string advice = DefaultAdvice,
        int minimumReasonLength = DefaultMinimumReasonLength)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentException.ThrowIfNullOrWhiteSpace(cutoffId);
        ArgumentNullException.ThrowIfNull(allowList);
        ArgumentNullException.ThrowIfNull(advice);

        var all = migrations.ToList();
        List<string> failures = [];
        var excused = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var entry in allowList)
        {
            var problems = EntryProblems(entry, all, cutoffId, minimumReasonLength);
            failures.AddRange(problems);
            if (problems.Count > 0)
            {
                continue;
            }

            if (!excused.TryGetValue(entry.MigrationId, out var rules))
            {
                rules = new HashSet<string>(StringComparer.Ordinal);
                excused[entry.MigrationId] = rules;
            }

            rules.UnionWith(entry.Rules);
        }

        foreach (var migration in all.Where(m => string.CompareOrdinal(m.Id, cutoffId) > 0))
        {
            var allowed = excused.GetValueOrDefault(migration.Id);
            foreach (var violation in migration.Violations)
            {
                if (allowed is not null && allowed.Contains(violation.Rule))
                {
                    continue;
                }

                failures.Add($"{GuardMarker} {migration.Id}: {violation.Rule}: {violation.Detail}. {advice}");
            }
        }

        return failures;
    }

    private static List<string> EntryProblems(MigrationAllowEntry entry, List<DiscoveredMigration> all, string cutoffId, int minimumReasonLength)
    {
        List<string> problems = [];

        var migration = all.FirstOrDefault(m => string.Equals(m.Id, entry.MigrationId, StringComparison.Ordinal));
        if (migration is null)
        {
            problems.Add($"{AllowListMarker} {entry.MigrationId}: no such migration. Remove the entry or correct its id.");
            return problems;
        }

        if (string.CompareOrdinal(entry.MigrationId, cutoffId) <= 0)
        {
            problems.Add($"{AllowListMarker} {entry.MigrationId}: grandfathered, so the entry excuses nothing. Remove it.");
            return problems;
        }

        if (entry.Reason.Trim().Length < minimumReasonLength)
        {
            problems.Add($"{AllowListMarker} {entry.MigrationId}: the reason must be at least {minimumReasonLength} characters.");
        }

        if (entry.Rules.Count == 0)
        {
            problems.Add($"{AllowListMarker} {entry.MigrationId}: names no rules.");
        }

        foreach (var rule in entry.Rules)
        {
            if (!migration.Violations.Any(v => string.Equals(v.Rule, rule, StringComparison.Ordinal)))
            {
                problems.Add($"{AllowListMarker} {entry.MigrationId}: stale, the migration has no {rule} violation to excuse. Remove the rule.");
            }
        }

        return problems;
    }
}
