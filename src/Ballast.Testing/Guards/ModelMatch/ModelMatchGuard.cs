// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

namespace Ballast.Testing.Guards.ModelMatch;

/// <summary>The settings of the model-matches-migrations rule: the closing advice, and the folders the suppression scan skips.</summary>
public sealed record ModelMatchOptions
{
    /// <summary>The advice that ends the model's message. Default: <c>Add a migration for the change.</c></summary>
    public string Advice { get; init; } = "Add a migration for the change.";

    /// <summary>
    /// The top-level folders the suppression scan skips. Folders whose name starts with a dot are skipped too. Default: tests, docs, and build
    /// output.
    /// </summary>
    public IReadOnlyList<string> SkippedTopLevel { get; init; } = ["tests", "docs", "bin", "obj", "node_modules"];
}

/// <summary>
/// The model-matches-migrations rule. A DbContext's model must match its migrations: a model change with no migration fails here by name,
/// rather than inside <c>Migrate()</c> at deploy time. The rule also forbids silencing EF's pending-model warning, because a suppressed warning
/// lets <c>Migrate()</c> run over a model that no migration describes.
/// </summary>
/// <remarks>
/// The check builds the context and asks EF whether the model has pending changes. It needs a provider's options, such as SQL Server's, and
/// never opens a connection, so the caller passes a factory that builds the context with options naming no reachable server.
/// </remarks>
public static class ModelMatchGuard
{
    /// <summary>The name of EF's warning that a model has no migration. Suppressing it is the thing the rule forbids.</summary>
    public const string PendingWarningName = "PendingModelChangesWarning";

    private static readonly Regex Suppression = new(
        @"\b(ConfigureWarnings|Ignore)\s*\([^;]*" + PendingWarningName,
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>
    /// The model problem of one context: one line when its model has changes that no migration covers, and empty when the model matches its
    /// migrations.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type. Its name starts the message.</typeparam>
    /// <param name="create">Builds the context with options that name no reachable server. Required. The context is disposed here.</param>
    /// <param name="options">The settings. Default: <see cref="ModelMatchOptions"/> with its defaults.</param>
    public static IReadOnlyList<string> Problems<TContext>(Func<TContext> create, ModelMatchOptions? options = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(create);
        var settings = options ?? new ModelMatchOptions();

        using var db = create();
        List<string> problems = [];
        if (db.Database.HasPendingModelChanges())
        {
            problems.Add($"{typeof(TContext).Name}'s model has changes that no migration covers. {settings.Advice}");
        }

        return problems;
    }

    /// <summary>
    /// The non-test C# sources of a repository, as a path with forward slashes from the root and the file's text: the C# files at the root,
    /// and every C# file under the other top-level folders, except the skipped ones and build output.
    /// </summary>
    /// <param name="root">The repository root. Required.</param>
    /// <param name="options">The settings. Default: <see cref="ModelMatchOptions"/> with its defaults.</param>
    /// <exception cref="DirectoryNotFoundException">The root does not exist.</exception>
    public static IReadOnlyList<(string RelativePath, string Source)> SuppressionSources(string root, ModelMatchOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The folder to scan does not exist: {root}");
        }

        var settings = options ?? new ModelMatchOptions();
        var skipped = new HashSet<string>(settings.SkippedTopLevel, StringComparer.OrdinalIgnoreCase);
        List<(string RelativePath, string Source)> sources = [];
        foreach (var path in Directory.EnumerateFiles(root, "*.cs"))
        {
            sources.Add((Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)));
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.') || skipped.Contains(name))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.Split('/').Any(segment => segment is "bin" or "obj" or "node_modules"))
                {
                    continue;
                }

                sources.Add((relative, File.ReadAllText(path)));
            }
        }

        return sources;
    }

    /// <summary>
    /// The suppression problems: one line per source that calls <c>ConfigureWarnings</c> or <c>Ignore</c> with the pending-model warning, outside
    /// a comment. Empty when no source suppresses it.
    /// </summary>
    /// <param name="sources">The sources, as <see cref="SuppressionSources"/> returns them. Required.</param>
    public static IReadOnlyList<string> SuppressionProblems(IEnumerable<(string RelativePath, string Source)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return sources
            .Where(source => Suppression.IsMatch(SourceScan.StripCSharpComments(source.Source)))
            .Select(source => source.RelativePath)
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path} suppresses {PendingWarningName}, so Migrate() can run over a model that no migration describes. Add the migration instead.")
            .ToList();
    }
}
