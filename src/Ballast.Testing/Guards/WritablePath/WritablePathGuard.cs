// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Testing.Guards.WritablePath;

/// <summary>
/// The settings of the writable-path rule: the folders that hold server code, what counts as a read of the content root, the names that
/// must no longer appear, and the words of the messages.
/// </summary>
public sealed record WritablePathOptions
{
    /// <summary>The folders under the repository root that hold server code, such as <c>ApiOne</c>. Required: at least one.</summary>
    public required IReadOnlyList<string> ServerTrees { get; init; }

    /// <summary>The folder names skipped anywhere in a tree. Default: build output and generated code.</summary>
    public IReadOnlyList<string> SkippedDirectories { get; init; } = ["bin", "obj", "Migrations", "CompiledModels", "publish"];

    /// <summary>The reads that name the content root or the app folder. Default: <see cref="WritablePathGuard.DefaultRootUse"/>.</summary>
    public Regex RootUse { get; init; } = WritablePathGuard.DefaultRootUse;

    /// <summary>Names that must not appear in server code at all, comments included. Default: none.</summary>
    public IReadOnlyList<string> RetiredNames { get; init; } = [];

    /// <summary>The name of the allow-list, which starts each problem message. Default: <c>WRITABLE-PATH-ALLOW-LIST</c>.</summary>
    public string ListName { get; init; } = "WRITABLE-PATH-ALLOW-LIST";

    /// <summary>The advice that ends each unexcused read's message.</summary>
    public string Advice { get; init; } = "Write under the data root instead, or add an entry with the reason the read is safe.";
}

/// <summary>
/// The writable-path rule. Server code writes only under its data root, never under the content root, which is read-only when the app
/// runs from its package, or under the folder the app was started from. Every read of either must sit on an allow-list line that says why
/// it is safe. An entry that no longer excuses a read fails too, so the list shrinks as the code is fixed.
/// </summary>
/// <remarks>
/// A source scan, not a proof: a path built in pieces across lines is not seen. The allow-list key is <c>path|line fragment</c>, split at
/// the first bar: the path is the file, and the fragment is a text the line must contain to be excused by that entry.
/// </remarks>
public static class WritablePathGuard
{
    /// <summary>The default reads: the content root, the web root, the base directory and the working directory.</summary>
    public static readonly Regex DefaultRootUse = new(
        @"ContentRootPath|WebRootPath|AppContext\.BaseDirectory|Directory\.GetCurrentDirectory",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] CSharpExtension = [".cs"];

    /// <summary>
    /// The server sources of a repository: every <c>.cs</c> file under the server trees, as a path with forward slashes from the root and
    /// the file's text. Nothing is removed from the text.
    /// </summary>
    /// <param name="root">The repository root. Required.</param>
    /// <param name="options">The settings. Required, with at least one server tree.</param>
    /// <exception cref="ArgumentException">No server tree is named, so the rule would check nothing.</exception>
    /// <exception cref="DirectoryNotFoundException">A server tree does not exist.</exception>
    public static IReadOnlyList<(string RelativePath, string Source)> ServerSources(string root, WritablePathOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ServerTrees.Count == 0)
        {
            throw new ArgumentException("At least one server tree is needed, or the rule checks nothing.", nameof(options));
        }

        List<(string RelativePath, string Source)> sources = [];
        foreach (var tree in options.ServerTrees)
        {
            foreach (var file in SourceScan.Files(Path.Combine(root, tree), CSharpExtension, options.SkippedDirectories, long.MaxValue))
            {
                sources.Add(($"{tree}/{file.RelativePath}", File.ReadAllText(file.FullPath)));
            }
        }

        return sources;
    }

    /// <summary>
    /// The writable-path problems in the given sources, as one line each. A read of the content root or the app folder that no allow-list
    /// entry excuses is a problem. So is an entry that is invalid or excuses nothing. Comments are removed before the reads are found, so
    /// a comment is not a read. The result is empty when the rule holds.
    /// </summary>
    /// <param name="sources">The server sources, as <see cref="ServerSources"/> returns them. Required.</param>
    /// <param name="options">The settings. Required.</param>
    /// <param name="allowList">The reviewed entries. Required. A key is <c>path|line fragment</c>.</param>
    public static IReadOnlyList<string> Problems(
        IEnumerable<(string RelativePath, string Source)> sources,
        WritablePathOptions options,
        IEnumerable<AllowEntry> allowList)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(allowList);

        List<string> malformed = [];
        List<AllowEntry> checkable = [];
        List<ParsedEntry> parsed = [];
        foreach (var entry in allowList)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                // The shared check names an entry with no key; nothing else can be read from it.
                checkable.Add(entry);
                continue;
            }

            var bar = entry.Key.IndexOf('|', StringComparison.Ordinal);
            if (bar <= 0 || bar == entry.Key.Length - 1 || entry.Key[(bar + 1)..].Trim().Length == 0)
            {
                malformed.Add($"{options.ListName} {entry.Key}: the key must be path|line fragment, so it excuses nothing. Correct it.");
                continue;
            }

            parsed.Add(new ParsedEntry(entry.Key, entry.Key[..bar], entry.Key[(bar + 1)..]));
            checkable.Add(entry);
        }

        var matched = new HashSet<string>(StringComparer.Ordinal);
        List<string> unexcused = [];
        foreach (var (path, source) in sources)
        {
            var lines = SourceScan.StripCSharpComments(source).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!options.RootUse.IsMatch(line))
                {
                    continue;
                }

                var excuse = parsed.FirstOrDefault(p =>
                    string.Equals(p.Path, path, StringComparison.Ordinal) && line.Contains(p.Fragment, StringComparison.Ordinal));
                if (excuse is null)
                {
                    unexcused.Add($"{options.ListName}: {path}:{i + 1}: {line.Trim()} reads the content root or the app folder outside the allow-list. {options.Advice}");
                }
                else
                {
                    matched.Add(excuse.Key);
                }
            }
        }

        List<string> problems = [.. AllowList.Problems(options.ListName, checkable, matched)];
        problems.AddRange(malformed);
        problems.AddRange(unexcused);
        return problems;
    }

    /// <summary>
    /// The retired names found in the sources: one line per file that still contains one. The text is read raw, comments included, so a
    /// name left in a comment still counts. Empty when no retired name is left.
    /// </summary>
    /// <param name="sources">The server sources, as <see cref="ServerSources"/> returns them. Required.</param>
    /// <param name="options">The settings. Required; its <see cref="WritablePathOptions.RetiredNames"/> are searched for.</param>
    public static IReadOnlyList<string> RetiredNameProblems(IEnumerable<(string RelativePath, string Source)> sources, WritablePathOptions options)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);

        List<string> problems = [];
        foreach (var (path, source) in sources)
        {
            foreach (var name in options.RetiredNames)
            {
                if (!string.IsNullOrEmpty(name) && source.Contains(name, StringComparison.Ordinal))
                {
                    problems.Add($"{options.ListName}: {path} contains the retired name '{name}', which must not appear in server code. Remove it.");
                }
            }
        }

        return problems;
    }

    private sealed record ParsedEntry(string Key, string Path, string Fragment);
}
