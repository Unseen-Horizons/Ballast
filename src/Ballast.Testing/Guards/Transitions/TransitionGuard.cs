// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Testing.Guards.Transitions;

/// <summary>The settings of the transition rule: which folders to read, which state enums and properties count, and the wording.</summary>
public sealed record TransitionOptions
{
    /// <summary>The folders under the repository root whose <c>.cs</c> files are read. Required: at least one.</summary>
    public required IReadOnlyList<string> Trees { get; init; }

    /// <summary>The folder names skipped anywhere in a tree. Default: build output and migrations.</summary>
    public IReadOnlyList<string> SkippedDirectories { get; init; } = ["bin", "obj", "Migrations"];

    /// <summary>
    /// The state-machine enum types whose tracked writes are checked, such as <c>TicketStatus</c>. A write is <c>x.Status = Enum.Value</c>.
    /// Required: at least one.
    /// </summary>
    public required IReadOnlyList<string> StateEnums { get; init; }

    /// <summary>The property that a tracked write sets. Default: <c>Status</c>.</summary>
    public string StatusProperty { get; init; } = "Status";

    /// <summary>
    /// The properties whose set-based writes are checked: <c>SetProperty(x =&gt; x.Name, ...)</c> with any value. Default: the state columns
    /// (<c>Status</c>, <c>Stage</c>, <c>UndoneAtUtc</c>, <c>EntriesJson</c>, <c>ReadingState</c>).
    /// </summary>
    public IReadOnlyList<string> SetBasedProperties { get; init; } = ["Status", "Stage", "UndoneAtUtc", "EntriesJson", "ReadingState"];

    /// <summary>The name of the allow-list, which starts each problem message. Default: <c>TRANSITION-ALLOW-LIST</c>.</summary>
    public string ListName { get; init; } = "TRANSITION-ALLOW-LIST";

    /// <summary>The advice that ends each unlisted write's message.</summary>
    public string Advice { get; init; } = "Make it a conditional claim (Where Status == expected, then ExecuteUpdate) or put it behind a transition check, then list it with its reason.";
}

/// <summary>
/// A reviewed write, as the allow-list names it: the file, the write's trimmed source line, how many times that line may occur in the file,
/// and why it is safe. A write that is not listed, or a line that occurs more or fewer times than its entry says, is a problem.
/// </summary>
/// <param name="RelativePath">The file, with forward slashes from the repository root.</param>
/// <param name="Line">The write's trimmed source line, exactly as the scan reports it.</param>
/// <param name="Count">How many times the line occurs in that file. At least 1.</param>
/// <param name="Reason">Why the write is safe. At least 20 characters.</param>
public sealed record TransitionAllowEntry(string RelativePath, string Line, int Count, string Reason);

/// <summary>A status write that the scan found.</summary>
/// <param name="RelativePath">The file the write is in.</param>
/// <param name="Line">The line of the write, counted from 1.</param>
/// <param name="Text">The write's trimmed source line.</param>
public sealed record StatusWrite(string RelativePath, int Line, string Text);

/// <summary>
/// The transition rule. A ticket, purchase, reservation or journey changes state only from a state the code names: either the write is a
/// conditional claim (a write whose <c>Where</c> names the status it must have) or it is a tracked write behind a transition check. An
/// unconditional <c>entity.Status = X</c> overwrites whatever a concurrent request already moved the row to. Every status write is listed with
/// the reason it is safe, and a new write fails until someone has looked at it.
/// </summary>
/// <remarks>
/// A line scan, not a parser. A line that starts with <c>//</c> or <c>*</c> is a comment and is skipped; other lines are read as written. The
/// allow-list key is <c>path: line</c>, and each entry also states how many times the line occurs.
/// </remarks>
public static class TransitionGuard
{
    /// <summary>
    /// The C# sources under the trees of a repository, as a path with forward slashes from the root and the file's text. Files whose path has
    /// a skipped folder are left out. Nothing is cut short.
    /// </summary>
    /// <param name="root">The repository root. Required.</param>
    /// <param name="options">The settings. Required.</param>
    /// <exception cref="ArgumentException">No tree is named, so the rule would check nothing.</exception>
    /// <exception cref="DirectoryNotFoundException">A tree does not exist.</exception>
    public static IReadOnlyList<(string RelativePath, string Source)> Sources(string root, TransitionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Trees.Count == 0)
        {
            throw new ArgumentException("At least one tree is needed, or the rule checks nothing.", nameof(options));
        }

        List<(string RelativePath, string Source)> sources = [];
        foreach (var tree in options.Trees)
        {
            foreach (var file in SourceScan.Files(Path.Combine(root, tree), CSharpExtension, options.SkippedDirectories, long.MaxValue))
            {
                sources.Add(($"{tree}/{file.RelativePath}", File.ReadAllText(file.FullPath)));
            }
        }

        return sources;
    }

    /// <summary>
    /// The status writes in one file's text. A comment line is skipped. Each write is a tracked write of a state enum or a set-based write of a
    /// state property. This is the scanner the rule runs on each file.
    /// </summary>
    /// <param name="code">The file's text. Required.</param>
    /// <param name="options">The settings. Required; <see cref="TransitionOptions.StateEnums"/> must not be empty.</param>
    /// <param name="relativePath">The file's path, recorded in each write. Default: empty.</param>
    public static IReadOnlyList<StatusWrite> Find(string code, TransitionOptions options, string relativePath = "")
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(relativePath);

        var patterns = Patterns(options);
        List<StatusWrite> found = [];
        Collect(code, patterns, relativePath, found);
        return found;
    }

    /// <summary>
    /// The transition problems in the given sources: each status write that no allow-list entry lists, each entry whose count does not match
    /// what the code holds, each entry that is invalid, and each entry that matches nothing. Empty when the rule holds.
    /// </summary>
    /// <param name="sources">The sources, as <see cref="Sources"/> returns them. Required.</param>
    /// <param name="options">The settings. Required.</param>
    /// <param name="allowList">The reviewed writes. Required.</param>
    public static IReadOnlyList<string> Problems(
        IEnumerable<(string RelativePath, string Source)> sources,
        TransitionOptions options,
        IEnumerable<TransitionAllowEntry> allowList)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(allowList);

        var patterns = Patterns(options);
        List<StatusWrite> writes = [];
        foreach (var (path, source) in sources)
        {
            Collect(source, patterns, path, writes);
        }

        var entries = allowList.ToList();
        var listed = entries.Select(entry => new AllowEntry(KeyOf(entry.RelativePath, entry.Line), entry.Reason)).ToList();
        var counts = writes
            .GroupBy(write => KeyOf(write.RelativePath, write.Text), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var listedKeys = listed.Select(entry => entry.Key).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
        var matched = counts.Keys.Where(listedKeys.Contains).ToList();

        List<string> problems = [.. AllowList.Problems(options.ListName, listed, matched)];

        var countChecked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = KeyOf(entry.RelativePath, entry.Line);
            if (key.Length == 0 || !countChecked.Add(key) || !counts.TryGetValue(key, out var seen))
            {
                continue;
            }

            if (entry.Count < 1)
            {
                problems.Add($"{options.ListName}: {entry.RelativePath}: `{entry.Line}` must be covered at least once. Remove the entry or correct its count.");
            }
            else if (seen > entry.Count)
            {
                problems.Add($"{options.ListName}: {entry.RelativePath}: {seen} writes of `{entry.Line}`, the allow-list covers {entry.Count}. Each write needs its own review.");
            }
            else if (seen < entry.Count)
            {
                problems.Add($"{options.ListName}: {entry.RelativePath}: `{entry.Line}` is listed {entry.Count} time(s) but the code has {seen}. Update or remove the entry.");
            }
        }

        foreach (var write in writes)
        {
            if (!listedKeys.Contains(KeyOf(write.RelativePath, write.Text)))
            {
                problems.Add($"{options.ListName}: {write.RelativePath}:{write.Line}: {write.Text} is not excused by a reviewed entry with a reason of at least {AllowList.DefaultMinimumReasonLength} characters. {options.Advice}");
            }
        }

        return problems;
    }

    private static readonly string[] CSharpExtension = [".cs"];

    private static string KeyOf(string relativePath, string line) =>
        string.IsNullOrWhiteSpace(relativePath) ? string.Empty : $"{relativePath}: {line}";

    private static (Regex Tracked, Regex SetBased) Patterns(TransitionOptions options)
    {
        if (options.StateEnums.Count == 0)
        {
            throw new ArgumentException("At least one state enum is needed, or the tracked writes would not be checked.", nameof(options));
        }

        var enums = string.Join("|", options.StateEnums.Select(Regex.Escape));
        var properties = string.Join("|", options.SetBasedProperties.Select(Regex.Escape));
        var tracked = new Regex(@"\." + Regex.Escape(options.StatusProperty) + @"\s*=\s*(" + enums + @")\.", RegexOptions.CultureInvariant);
        var setBased = new Regex(@"SetProperty\(\s*\w+\s*=>\s*\w+\.(" + properties + @")\s*,", RegexOptions.CultureInvariant);
        return (tracked, setBased);
    }

    private static void Collect(string code, (Regex Tracked, Regex SetBased) patterns, string relativePath, List<StatusWrite> into)
    {
        var lines = code.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].Trim();
            if (text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith('*'))
            {
                continue;
            }

            if (patterns.Tracked.IsMatch(text) || patterns.SetBased.IsMatch(text))
            {
                into.Add(new StatusWrite(relativePath, i + 1, text));
            }
        }
    }
}
