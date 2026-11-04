// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace Ballast.Testing.Guards;

/// <summary>A text file found by <see cref="SourceScan.Files"/>.</summary>
/// <param name="RelativePath">The path relative to the scanned root, with forward slashes.</param>
/// <param name="FullPath">The path on disk.</param>
public sealed record SourceFile(string RelativePath, string FullPath);

/// <summary>Reads a repository's source for the guards: which files to read, and the code of a C# file without its comments.</summary>
public static class SourceScan
{
    /// <summary>The folders every scan skips by default: build output, package folders and the IDE's state.</summary>
    public static readonly IReadOnlyList<string> DefaultSkippedDirectories = ["bin", "obj", "node_modules", ".vs"];

    /// <summary>The largest file a scan reads by default: 3 MB. A bigger file is skipped, not truncated.</summary>
    public const long DefaultMaxFileBytes = 3_000_000;

    /// <summary>
    /// The files under <paramref name="root"/> whose extension is in <paramref name="extensions"/>, in no particular order. A file
    /// whose path has a skipped folder anywhere in it is left out, and so is a file over <paramref name="maxBytes"/>.
    /// </summary>
    /// <param name="root">The folder to scan. Must exist.</param>
    /// <param name="extensions">Extensions with the dot, such as <c>.cs</c>. Compared without regard to case.</param>
    /// <param name="skippedDirectories">Folder names to skip. Default: <see cref="DefaultSkippedDirectories"/>.</param>
    /// <param name="maxBytes">The largest file to return.</param>
    /// <exception cref="DirectoryNotFoundException">The root does not exist.</exception>
    public static IEnumerable<SourceFile> Files(
        string root,
        IEnumerable<string> extensions,
        IEnumerable<string>? skippedDirectories = null,
        long maxBytes = DefaultMaxFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(extensions);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The folder to scan does not exist: {root}");
        }

        var wanted = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        var skipped = new HashSet<string>(skippedDirectories ?? DefaultSkippedDirectories, StringComparer.OrdinalIgnoreCase);
        return EnumerateFiles(root, wanted, skipped, maxBytes);
    }

    private static IEnumerable<SourceFile> EnumerateFiles(string root, HashSet<string> wanted, HashSet<string> skipped, long maxBytes)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments[..^1].Any(skipped.Contains))
            {
                continue;
            }

            if (!wanted.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            if (new FileInfo(path).Length > maxBytes)
            {
                continue;
            }

            yield return new SourceFile(relative, path);
        }
    }

    /// <summary>
    /// The code of a C# source with its comments removed: <c>//</c> to the end of the line, and <c>/* */</c>. String, verbatim, raw and
    /// interpolated string literals and character literals are read whole and kept, so a <c>//</c> inside a URL literal is not taken for
    /// a comment, and a quote inside a character literal does not open a string. Every line break is kept, including the ones inside a
    /// block comment, so a line number in the result is the line number in the source.
    /// </summary>
    /// <remarks>
    /// A tripwire's reader, not a parser. An interpolation hole that holds a string literal (<c>$"{a ?? "b"}"</c>) is read as the end of
    /// the outer string at the inner quote, so text after it in that same literal is read as code. That can only make a comment in such a
    /// literal count as code, which a guard then reports as a finding to read, never a missed finding.
    /// </remarks>
    /// <param name="source">The C# source text. Required.</param>
    public static string StripCSharpComments(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '/' && Next(source, i) == '/')
            {
                // A line comment ends before the line break, which is kept.
                var end = source.IndexOfAny(['\r', '\n'], i);
                i = end < 0 ? source.Length : end;
                continue;
            }

            if (c == '/' && Next(source, i) == '*')
            {
                var close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? source.Length : close + 2;
                for (var k = i; k < stop; k++)
                {
                    if (source[k] == '\n')
                    {
                        output.Append('\n');
                    }
                }

                i = stop;
                continue;
            }

            if (c == '"')
            {
                var end = EndOfStringLiteral(source, i);
                output.Append(source, i, end - i);
                i = end;
                continue;
            }

            if (c == '\'')
            {
                var end = EndOfCharLiteral(source, i);
                output.Append(source, i, end - i);
                i = end;
                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    private static char Next(string source, int i) => i + 1 < source.Length ? source[i + 1] : '\0';

    // The end (exclusive) of the string literal whose opening quote is at i.
    private static int EndOfStringLiteral(string source, int i)
    {
        // A verbatim literal (@" or $@") is decided first: @"""a""" is a verbatim literal, not a raw one. Raw literals take no @.
        var verbatim = i > 0 && (source[i - 1] == '@' || (source[i - 1] == '$' && i > 1 && source[i - 2] == '@'));
        if (verbatim)
        {
            return EndOfVerbatimLiteral(source, i + 1);
        }

        var quotes = RunLength(source, i, '"');
        return quotes >= 3 ? EndOfRawLiteral(source, i + quotes, quotes) : EndOfRegularLiteral(source, i + 1);
    }

    // A regular or interpolated literal: escapes are a backslash and the next character; a line break ends it unfinished.
    private static int EndOfRegularLiteral(string source, int j)
    {
        while (j < source.Length)
        {
            var c = source[j];
            if (c == '\\')
            {
                j += 2;
                continue;
            }

            if (c == '"')
            {
                return j + 1;
            }

            if (c is '\r' or '\n')
            {
                return j;
            }

            j++;
        }

        return source.Length;
    }

    // A verbatim literal: a doubled quote is one quote, a single quote ends it, and line breaks are part of it.
    private static int EndOfVerbatimLiteral(string source, int j)
    {
        while (j < source.Length)
        {
            if (source[j] == '"')
            {
                if (j + 1 < source.Length && source[j + 1] == '"')
                {
                    j += 2;
                    continue;
                }

                return j + 1;
            }

            j++;
        }

        return source.Length;
    }

    // A raw literal: it closes at the first run of at least as many quotes as opened it.
    private static int EndOfRawLiteral(string source, int j, int quotes)
    {
        while (j < source.Length)
        {
            if (source[j] == '"')
            {
                var run = RunLength(source, j, '"');
                if (run >= quotes)
                {
                    return j + run;
                }

                j += run;
                continue;
            }

            j++;
        }

        return source.Length;
    }

    // A character literal: an escape is a backslash and the next character; a line break ends it unfinished.
    private static int EndOfCharLiteral(string source, int i)
    {
        var j = i + 1;
        while (j < source.Length)
        {
            var c = source[j];
            if (c == '\\')
            {
                j += 2;
                continue;
            }

            if (c == '\'')
            {
                return j + 1;
            }

            if (c is '\r' or '\n')
            {
                return j;
            }

            j++;
        }

        return source.Length;
    }

    private static int RunLength(string source, int start, char symbol)
    {
        var n = 0;
        while (start + n < source.Length && source[start + n] == symbol)
        {
            n++;
        }

        return n;
    }
}
