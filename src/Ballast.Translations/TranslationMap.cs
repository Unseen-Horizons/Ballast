// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// The texts of one file: its entries in file order, and the problems found while the file was read or the map was built.
/// A map comes from <see cref="ResxReader"/> or <see cref="JsonStringsReader"/>, or from entries given to this constructor, which
/// is the way to use another file format.
/// </summary>
public sealed class TranslationMap
{
    private readonly Dictionary<string, TranslationEntry> _index = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds a map from entries in file order.
    /// </summary>
    /// <param name="name">The name of the file. Every problem about this map names it. Required.</param>
    /// <param name="entries">The entries, in file order. Each entry and each value must not be null.</param>
    /// <param name="readProblems">
    /// Problems the reader found before the entries were built, or null. They are kept as they are, and come first in
    /// <see cref="Problems"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// An entry whose key is null, empty or only white space is not kept, and is reported as
    /// <see cref="TranslationProblemKind.UnnamedEntry"/>. A key that is already in the map is not kept a second time: its later entry
    /// is reported as <see cref="TranslationProblemKind.DuplicateKey"/>, and the first value stays. The map therefore holds each key once.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or only white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null, or an entry or an entry's value is null.</exception>
    public TranslationMap(string name, IEnumerable<TranslationEntry> entries, IEnumerable<TranslationProblem>? readProblems = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(entries);

        Name = name;
        List<TranslationEntry> kept = [];
        List<TranslationProblem> problems = [];
        if (readProblems is not null)
        {
            problems.AddRange(readProblems);
        }

        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.Value);

            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                problems.Add(new TranslationProblem(name, null, entry.Line, TranslationProblemKind.UnnamedEntry,
                    "An entry has no name, so it is not kept."));
                continue;
            }

            if (_index.TryGetValue(entry.Key, out var first))
            {
                var firstLine = first.Line is int line ? $" (first at line {line})" : string.Empty;
                problems.Add(new TranslationProblem(name, entry.Key, entry.Line, TranslationProblemKind.DuplicateKey,
                    $"\"{entry.Key}\" appears more than once{firstLine}; the first value is used."));
                continue;
            }

            _index.Add(entry.Key, entry);
            kept.Add(entry);
        }

        Entries = kept.AsReadOnly();
        Problems = problems.AsReadOnly();
    }

    /// <summary>
    /// The name of the file. Every problem about this map names it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The entries in file order. Each key appears once: a duplicate is not in this list.
    /// </summary>
    public IReadOnlyList<TranslationEntry> Entries { get; }

    /// <summary>
    /// The problems found while the map was read or built, in the order they were found. It is empty for a clean file.
    /// </summary>
    public IReadOnlyList<TranslationProblem> Problems { get; }

    /// <summary>
    /// The number of keys in the map.
    /// </summary>
    public int Count => _index.Count;

    /// <summary>
    /// Tells whether the map holds a key. The comparison is exact (ordinal, letter case included).
    /// </summary>
    /// <param name="key">The key to find. Required.</param>
    /// <returns>True when the key is in the map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public bool ContainsKey(string key) => _index.ContainsKey(key);

    /// <summary>
    /// Finds the text of a key.
    /// </summary>
    /// <param name="key">The key to find. Required.</param>
    /// <param name="value">The text of the key, or the empty string when the key is not in the map.</param>
    /// <returns>True when the key is in the map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public bool TryGetValue(string key, out string value)
    {
        if (_index.TryGetValue(key, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// The entry of a key, with its line, or null when the key is not in the map.
    /// </summary>
    internal TranslationEntry? EntryOf(string key) => _index.TryGetValue(key, out var entry) ? entry : null;
}
