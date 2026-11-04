// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Translations;

/// <summary>
/// Finds the literal keys that source text asks for, and reports each key that is not in the base map.
/// </summary>
/// <remarks>
/// <para>
/// Only the caller knows how its code asks for a text, so the caller gives the pattern. The pattern must have a named group (by
/// default <c>key</c>) that captures the key. Every match that captures a key is one use of that key, so one pattern can list the
/// several ways a source asks for a text, as alternatives that name the same group.
/// </para>
/// <para>
/// The scan does not understand the source language. A pattern that matches inside a comment or a string literal counts there
/// too, so the pattern must be written to exclude what the source does not really ask for.
/// </para>
/// </remarks>
public static class LiteralKeyScan
{
    /// <summary>
    /// Scans source texts for keys.
    /// </summary>
    /// <param name="baseMap">The base map that every key must be in. Required.</param>
    /// <param name="pattern">The pattern. It must have a group named <paramref name="keyGroup"/>. Required.</param>
    /// <param name="sources">The source texts, each with the name it is reported under. Required.</param>
    /// <param name="keyGroup">The name of the group that captures the key. Default <c>key</c>.</param>
    /// <returns>The number of uses found, and a problem for each use whose key is not in the base map.</returns>
    /// <exception cref="ArgumentNullException">A required argument, a source's text, or <paramref name="keyGroup"/>, is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="keyGroup"/> is empty or only white space, or <paramref name="pattern"/> has no group of that name.
    /// </exception>
    public static LiteralKeyScanResult Scan(
        TranslationMap baseMap,
        Regex pattern,
        IEnumerable<(string File, string Text)> sources,
        string keyGroup = "key")
    {
        ArgumentNullException.ThrowIfNull(baseMap);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyGroup);
        if (!pattern.GetGroupNames().Contains(keyGroup, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The pattern has no group named \"{keyGroup}\".", nameof(pattern));
        }

        var uses = 0;
        List<TranslationProblem> problems = [];
        foreach (var (file, text) in sources)
        {
            ArgumentNullException.ThrowIfNull(text);
            var position = 0;
            var line = 1;
            foreach (Match match in pattern.Matches(text))
            {
                var group = match.Groups[keyGroup];
                if (!group.Success)
                {
                    continue;
                }

                // Matches come in order, so the line is counted forward from the last key, not from the start of the text.
                for (; position < group.Index; position++)
                {
                    if (text[position] == '\n')
                    {
                        line++;
                    }
                }

                uses++;
                if (!baseMap.ContainsKey(group.Value))
                {
                    problems.Add(new TranslationProblem(file, group.Value, line, TranslationProblemKind.UnknownKey,
                        $"\"{group.Value}\" is used here but is not in {baseMap.Name}."));
                }
            }
        }

        return new LiteralKeyScanResult(uses, problems.AsReadOnly());
    }

    /// <summary>
    /// Scans source files for keys. Each file is read as text (a byte order mark is detected, and the default is UTF-8) when the
    /// scan reaches it, and is reported under the path it was given.
    /// </summary>
    /// <param name="baseMap">The base map that every key must be in. Required.</param>
    /// <param name="pattern">The pattern. It must have a group named <paramref name="keyGroup"/>. Required.</param>
    /// <param name="paths">The source files. Required.</param>
    /// <param name="keyGroup">The name of the group that captures the key. Default <c>key</c>.</param>
    /// <returns>The number of uses found, and a problem for each use whose key is not in the base map.</returns>
    /// <exception cref="ArgumentNullException">A required argument, a path, or <paramref name="keyGroup"/>, is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="keyGroup"/> is empty or only white space, or <paramref name="pattern"/> has no group of that name.
    /// </exception>
    /// <exception cref="FileNotFoundException">A file does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder of a file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">A file cannot be opened for reading.</exception>
    public static LiteralKeyScanResult ScanFiles(
        TranslationMap baseMap,
        Regex pattern,
        IEnumerable<string> paths,
        string keyGroup = "key")
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Scan(baseMap, pattern, ReadSources(paths), keyGroup);
    }

    private static IEnumerable<(string File, string Text)> ReadSources(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            ArgumentNullException.ThrowIfNull(path);
            yield return (path, File.ReadAllText(path));
        }
    }
}
