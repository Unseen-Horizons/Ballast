// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Guards;
using Ballast.Testing.Guards.ModelMatch;

using Microsoft.EntityFrameworkCore;

namespace Ballast.Testing.Tests.Guards;

/// <summary>An entity with no migration behind it, so its model has a pending change.</summary>
public sealed class PendingRow
{
    public int Id { get; set; }
}

/// <summary>A context with one entity and no migrations: its model has changes that no migration covers.</summary>
public sealed class PendingModelContext : DbContext
{
    public PendingModelContext(DbContextOptions<PendingModelContext> options)
        : base(options)
    {
    }

    public DbSet<PendingRow> Rows => Set<PendingRow>();
}

/// <summary>A context with no entity: its model is empty, so it has no pending change.</summary>
public sealed class EmptyModelContext : DbContext
{
    public EmptyModelContext(DbContextOptions<EmptyModelContext> options)
        : base(options)
    {
    }
}

/// <summary>The model-matches-migrations rule and the check that the pending-model warning is not suppressed.</summary>
public class ModelMatchGuardTests
{
    // Options that name a server nothing listens on. The check builds the model and never opens a connection.
    private static DbContextOptions<T> Unused<T>()
        where T : DbContext
    {
        return new DbContextOptionsBuilder<T>().UseSqlServer("Server=.;Database=unused").Options;
    }

    [Fact]
    public void A_model_with_a_change_no_migration_covers_is_reported_by_name()
    {
        var problem = Assert.Single(ModelMatchGuard.Problems(() => new PendingModelContext(Unused<PendingModelContext>())));

        Assert.Equal("PendingModelContext's model has changes that no migration covers. Add a migration for the change.", problem);
    }

    [Fact]
    public void A_model_with_no_pending_change_has_no_problems()
    {
        Assert.Empty(ModelMatchGuard.Problems(() => new EmptyModelContext(Unused<EmptyModelContext>())));
    }

    [Fact]
    public void The_advice_is_an_option()
    {
        var problem = Assert.Single(ModelMatchGuard.Problems(
            () => new PendingModelContext(Unused<PendingModelContext>()),
            new ModelMatchOptions { Advice = "Add the migration and regenerate the compiled model." }));

        Assert.EndsWith(" Add the migration and regenerate the compiled model.", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_planted_suppression_is_reported_and_a_comment_or_a_mention_is_not()
    {
        var planted = "options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));";

        var problem = Assert.Single(ModelMatchGuard.SuppressionProblems([("src/Planted.cs", planted)]));
        Assert.StartsWith("src/Planted.cs suppresses PendingModelChangesWarning", problem, StringComparison.Ordinal);

        Assert.Empty(ModelMatchGuard.SuppressionProblems(
        [
            ("src/Comment.cs", "// the old build suppressed PendingModelChangesWarning\n/* PendingModelChangesWarning */\nvar x = 1;"),
            ("src/Mentioned.cs", "if (ex is RelationalEventId.PendingModelChangesWarning) throw;"),
        ]));
    }

    [Fact]
    public void The_planted_suppression_is_caught_and_a_clean_source_passes()
    {
        var planted = "options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));";

        GuardSelfTest.AssertCatches(source => ModelMatchGuard.SuppressionProblems([("planted.cs", source)]), planted, "planted.cs suppresses");
        GuardSelfTest.AssertPasses(source => ModelMatchGuard.SuppressionProblems([("planted.cs", source)]), "var options = Build();");
    }

    [Fact]
    public void Suppression_sources_read_the_root_and_the_non_test_folders_and_skip_tests_docs_and_build_output()
    {
        using var repo = new TempRepository();
        repo.Write("Root.cs", "class R {}");
        repo.Write("ApiOne/Store.cs", "class A {}");
        repo.Write("ApiOne/bin/Built.cs", "class B {}");
        repo.Write("tests/Test.cs", "class T {}");
        repo.Write("docs/Doc.cs", "class D {}");
        repo.Write(".hidden/Hide.cs", "class H {}");

        var sources = ModelMatchGuard.SuppressionSources(repo.Root).Select(source => source.RelativePath).Order(StringComparer.Ordinal).ToList();

        string[] expected = ["ApiOne/Store.cs", "Root.cs"];
        Assert.Equal(expected, sources);
    }

    [Fact]
    public void Suppression_sources_refuse_a_missing_root()
    {
        using var repo = new TempRepository();

        Assert.Throws<DirectoryNotFoundException>(() => ModelMatchGuard.SuppressionSources(Path.Combine(repo.Root, "missing")));
    }

    [Fact]
    public void The_skipped_top_level_folders_are_an_option()
    {
        using var repo = new TempRepository();
        repo.Write("tests/Test.cs", "class T {}");

        var sources = ModelMatchGuard.SuppressionSources(repo.Root, new ModelMatchOptions { SkippedTopLevel = [] });

        Assert.Equal("tests/Test.cs", Assert.Single(sources).RelativePath);
    }
}
