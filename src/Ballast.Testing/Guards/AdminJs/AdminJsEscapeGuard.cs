// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.RegularExpressions;

namespace Ballast.Testing.Guards.AdminJs;

/// <summary>The settings of the admin-page escaping rule: which scripts to read, which names count as text, and the wording.</summary>
public sealed record AdminJsOptions
{
    /// <summary>The folders under the repository root whose <c>.js</c> files are scanned. Required: at least one.</summary>
    public required IReadOnlyList<string> Folders { get; init; }

    /// <summary>The last property names that count as text from data. Default: <see cref="AdminJsEscapeGuard.DefaultTextNames"/>.</summary>
    public IReadOnlyList<string> TextNames { get; init; } = AdminJsEscapeGuard.DefaultTextNames;

    /// <summary>
    /// The fewest scripts the scan must find, so that a wrong path fails rather than passing with nothing read. Default: 1. An app that
    /// expects a known number of scripts sets it to that number.
    /// </summary>
    public int MinimumFileCount { get; init; } = 1;

    /// <summary>The name of the allow-list, which starts each problem message. Default: <c>ADMIN-JS-ALLOW-LIST</c>.</summary>
    public string ListName { get; init; } = "ADMIN-JS-ALLOW-LIST";

    /// <summary>The advice that ends each unescaped hit's message.</summary>
    public string Advice { get; init; } = "Escape it with the file's escape helper, such as esc(...) or encodeURIComponent(...), before it goes into the template.";
}

/// <summary>A text-like value that a template literal puts into HTML without escaping it.</summary>
/// <param name="Line">The line of the <c>${</c> that holds it.</param>
/// <param name="Expression">The expression, with its whitespace collapsed to single spaces.</param>
public sealed record UnescapedText(int Line, string Expression);

/// <summary>
/// The admin-page escaping rule. Admin pages build HTML in template literals and assign it to <c>innerHTML</c>. Text from data (names,
/// titles, messages, urls) reaches those templates, so a bare <c>${row.name}</c> is stored cross-site scripting in an admin session. Text
/// from data goes through the file's escape helper. The scan flags a text-like member path interpolated as it is into a template that
/// builds HTML.
/// </summary>
/// <remarks>
/// A tripwire, not a parser. It reads one file's text and follows strings, comments, regular expressions and nested templates. A value the
/// scan cannot follow is reported for a reader to check, never passed silently. The allow-list key is <c>path|expression</c>, split at the
/// first bar, and it must name the expression exactly as the scan reports it.
/// </remarks>
public static class AdminJsEscapeGuard
{
    /// <summary>The names that count as text from data: a name, title, message, url and similar.</summary>
    public static readonly IReadOnlyList<string> DefaultTextNames =
    [
        "name", "title", "message", "text", "description", "label", "headsign", "reason", "error", "detail", "note", "comment", "summary", "value", "url",
    ];

    private const string Identifier = @"[A-Za-z_$][\w$]*";

    private static readonly string[] JavaScriptExtension = [".js"];

    // A member path: a.b, a?.b, a[b].c, a.b[0].c. A bare identifier is not a member path.
    private static readonly Regex MemberPath = new(
        $@"^{Identifier}(?:\??\.{Identifier}|\?\.\[[^\]]*\]|\[[^\]]*\])+$",
        RegexOptions.CultureInvariant);

    // `path || 'literal'` and `path ?? ''` are still the bare value.
    private static readonly Regex Fallback = new(
        @"^(?<path>.+?)\s*(?:\|\||\?\?)\s*(?:'[^']*'|""[^""]*""|``|\d+)$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex LastIdentifier = new($@"({Identifier})$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The scan of one file's text: every text-like path interpolated into a template literal that builds HTML, with its line. Comments are
    /// skipped, and so are regular expression literals and quoted strings.
    /// </summary>
    /// <param name="code">The file's text. Required.</param>
    /// <param name="textNames">The last property names that count as text. Default: <see cref="DefaultTextNames"/>.</param>
    /// <exception cref="ArgumentException">The name list is empty.</exception>
    public static IReadOnlyList<UnescapedText> Scan(string code, IReadOnlyList<string>? textNames = null)
    {
        ArgumentNullException.ThrowIfNull(code);

        var scanner = new TemplateScan(code, TextTail(textNames ?? DefaultTextNames));
        scanner.Walk(0, code.Length);
        return scanner.Hits;
    }

    /// <summary>
    /// The scripts under the folders of a repository, as a path with forward slashes from the root and the file's text. Nothing is skipped
    /// and no file is cut short.
    /// </summary>
    /// <param name="root">The repository root. Required.</param>
    /// <param name="options">The settings. Required, with at least one folder.</param>
    /// <exception cref="ArgumentException">No folder is named, so the rule would check nothing.</exception>
    /// <exception cref="DirectoryNotFoundException">A folder does not exist.</exception>
    /// <exception cref="InvalidOperationException">Fewer scripts were found than <see cref="AdminJsOptions.MinimumFileCount"/> requires.</exception>
    public static IReadOnlyList<(string RelativePath, string Source)> Sources(string root, AdminJsOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Folders.Count == 0)
        {
            throw new ArgumentException("At least one folder is needed, or the rule checks nothing.", nameof(options));
        }

        List<(string RelativePath, string Source)> sources = [];
        foreach (var folder in options.Folders)
        {
            foreach (var file in SourceScan.Files(Path.Combine(root, folder), JavaScriptExtension, [], long.MaxValue))
            {
                sources.Add(($"{folder}/{file.RelativePath}", File.ReadAllText(file.FullPath)));
            }
        }

        if (sources.Count < options.MinimumFileCount)
        {
            throw new InvalidOperationException(
                $"The scan found {sources.Count} script(s) under the folders; at least {options.MinimumFileCount} were expected. The path is wrong, or the folders are empty.");
        }

        return sources;
    }

    /// <summary>
    /// The escaping problems in the given scripts: each text-like value that no allow-list entry excuses, each entry that is invalid, and
    /// each entry that excuses nothing. Empty when the rule holds.
    /// </summary>
    /// <param name="sources">The scripts, as <see cref="Sources"/> returns them. Required.</param>
    /// <param name="options">The settings. Required.</param>
    /// <param name="allowList">The reviewed entries. Required. A key is <c>path|expression</c>.</param>
    public static IReadOnlyList<string> Problems(
        IEnumerable<(string RelativePath, string Source)> sources,
        AdminJsOptions options,
        IEnumerable<AllowEntry> allowList)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(allowList);

        var entries = allowList.ToList();
        var keys = entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Key)).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var matched = new HashSet<string>(StringComparer.Ordinal);
        List<string> unexcused = [];
        foreach (var (path, source) in sources)
        {
            foreach (var hit in Scan(source, options.TextNames))
            {
                var key = $"{path}|{hit.Expression}";
                if (keys.Contains(key))
                {
                    matched.Add(key);
                }
                else
                {
                    unexcused.Add($"{options.ListName}: {path}:{hit.Line}: ${{{hit.Expression}}} is text from data in an HTML template, not escaped. {options.Advice}");
                }
            }
        }

        List<string> problems = [.. AllowList.Problems(options.ListName, entries, matched)];
        problems.AddRange(unexcused);
        return problems;
    }

    private static Regex TextTail(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            throw new ArgumentException("At least one text name is needed, or every value would count as text.", nameof(names));
        }

        return new Regex("(" + string.Join("|", names.Select(Regex.Escape)) + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>The state of one scan: the code, the text-name pattern and the hits found so far.</summary>
    private sealed class TemplateScan
    {
        private readonly string _code;
        private readonly Regex _textTail;
        private readonly List<UnescapedText> _hits = [];

        public TemplateScan(string code, Regex textTail)
        {
            _code = code;
            _textTail = textTail;
        }

        public IReadOnlyList<UnescapedText> Hits => _hits;

        /// <summary>Scans code[start..end) for template literals, recording those in the scan and following their expressions.</summary>
        public void Walk(int start, int end)
        {
            var i = start;
            while (i < end)
            {
                var c = _code[i];
                if (c == '/' && i + 1 < end && _code[i + 1] == '/')
                {
                    i = SkipLine(i, end);
                    continue;
                }

                if (c == '/' && i + 1 < end && _code[i + 1] == '*')
                {
                    var close = _code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 || close >= end ? end : close + 2;
                    continue;
                }

                if (c is '\'' or '"')
                {
                    i = SkipQuoted(i, end);
                    continue;
                }

                if (c == '/' && PrecedesRegex(i))
                {
                    i = SkipRegex(i, end);
                    continue;
                }

                if (c == '`')
                {
                    i = ParseTemplate(i, end, record: true);
                    continue;
                }

                i++;
            }
        }

        // Parses the template literal that opens at `open`, and returns the index after its closing backtick. When recording, a text-like
        // value in an HTML template is kept, and the expressions are walked. A template read only to find its end is not recorded again.
        private int ParseTemplate(int open, int end, bool record)
        {
            var literal = new StringBuilder();
            List<(int Start, int End)> expressions = [];
            var i = open + 1;
            while (i < end)
            {
                var c = _code[i];
                if (c == '\\')
                {
                    literal.Append(c);
                    i += 2;
                    continue;
                }

                if (c == '`')
                {
                    i++;
                    break;
                }

                if (c == '$' && i + 1 < end && _code[i + 1] == '{')
                {
                    var exprStart = i + 2;
                    var exprEnd = MatchingBrace(exprStart, end);
                    expressions.Add((exprStart, exprEnd));
                    i = exprEnd + 1;
                    continue;
                }

                literal.Append(c);
                i++;
            }

            if (!record)
            {
                return i;
            }

            if (literal.ToString().Contains('<'))
            {
                foreach (var (start, stop) in expressions)
                {
                    var text = _code[start..stop];
                    if (IsTextLikePath(text))
                    {
                        _hits.Add(new UnescapedText(LineOf(start), Regex.Replace(text.Trim(), @"\s+", " ")));
                    }
                }
            }

            foreach (var (start, stop) in expressions)
            {
                Walk(start, stop);
            }

            return i;
        }

        // The index of the brace that closes a ${ whose content starts at i.
        private int MatchingBrace(int i, int end)
        {
            var depth = 1;
            while (i < end)
            {
                var c = _code[i];
                if (c == '/' && i + 1 < end && _code[i + 1] == '/')
                {
                    i = SkipLine(i, end);
                    continue;
                }

                if (c is '\'' or '"')
                {
                    i = SkipQuoted(i, end);
                    continue;
                }

                if (c == '/' && PrecedesRegex(i))
                {
                    i = SkipRegex(i, end);
                    continue;
                }

                if (c == '`')
                {
                    i = ParseTemplate(i, end, record: false);
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }

                i++;
            }

            return end;
        }

        private int SkipLine(int i, int end)
        {
            var newline = _code.IndexOf('\n', i);
            return newline < 0 || newline >= end ? end : newline + 1;
        }

        private int SkipQuoted(int i, int end)
        {
            var quote = _code[i];
            i++;
            while (i < end)
            {
                if (_code[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (_code[i] == quote || _code[i] == '\n')
                {
                    return i + 1;
                }

                i++;
            }

            return end;
        }

        private int SkipRegex(int i, int end)
        {
            i++;
            var inClass = false;
            while (i < end)
            {
                var c = _code[i];
                if (c == '\\')
                {
                    i += 2;
                    continue;
                }

                if (c == '\n')
                {
                    return i + 1;
                }

                if (c == '[')
                {
                    inClass = true;
                }
                else if (c == ']')
                {
                    inClass = false;
                }
                else if (c == '/' && !inClass)
                {
                    return i + 1;
                }

                i++;
            }

            return end;
        }

        // Whether a slash opens a regular expression rather than dividing: it follows an operator, a bracket, or the word return.
        private bool PrecedesRegex(int slash)
        {
            for (var i = slash - 1; i >= 0; i--)
            {
                if (char.IsWhiteSpace(_code[i]))
                {
                    continue;
                }

                return "(,=:[!&|?{};+-*%<>~^".Contains(_code[i])
                    || (i >= 5 && string.Equals(_code.Substring(i - 5, 6), "return", StringComparison.Ordinal));
            }

            return true;
        }

        private bool IsTextLikePath(string expression)
        {
            var e = expression.Trim();
            var fallback = Fallback.Match(e);
            if (fallback.Success)
            {
                e = fallback.Groups["path"].Value.Trim();
            }

            if (!MemberPath.IsMatch(e) || e.EndsWith(']'))
            {
                return false;
            }

            var last = LastIdentifier.Match(e);
            return last.Success && _textTail.IsMatch(last.Groups[1].Value);
        }

        private int LineOf(int index)
        {
            var line = 1;
            for (var i = 0; i < index && i < _code.Length; i++)
            {
                if (_code[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }
    }
}
