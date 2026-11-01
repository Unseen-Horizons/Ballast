// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// The parity checks. Each translation is compared with the base file, and every difference is a <see cref="TranslationProblem"/>.
/// The checks return a list and do not throw for a difference, so a test asserts that the list is empty and prints every problem
/// when it is not.
/// </summary>
public static class TranslationParity
{
    /// <summary>
    /// Checks one translation against the base file.
    /// </summary>
    /// <param name="baseMap">The base file, for example the English texts. Required.</param>
    /// <param name="translation">The translation to check. Required.</param>
    /// <param name="options">The options, or null for the defaults.</param>
    /// <returns>The problems, in a fixed order. The list is empty when the translation is in parity.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The allow-list has a null, empty or white-space entry.</exception>
    public static IReadOnlyList<TranslationProblem> Check(TranslationMap baseMap, TranslationMap translation, TranslationParityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(translation);
        return Check(baseMap, [translation], options);
    }

    /// <summary>
    /// Checks several translations against the base file. The base file's own problems are reported once, and each translation's
    /// problems are reported after them, in the order the translations are given.
    /// </summary>
    /// <param name="baseMap">The base file, for example the English texts. Required.</param>
    /// <param name="translations">The translations to check. Required. Each must not be null.</param>
    /// <param name="options">The options, or null for the defaults.</param>
    /// <returns>The problems, in a fixed order. The list is empty when every translation is in parity.</returns>
    /// <exception cref="ArgumentNullException">A required argument, or a translation in the list, is null.</exception>
    /// <exception cref="ArgumentException">The allow-list has a null, empty or white-space entry.</exception>
    /// <remarks>
    /// <para>The problems come in this order:</para>
    /// <list type="number">
    /// <item>The base file's problems from its reader (a duplicate key, an unnamed entry, a non-string JSON value), then its blank texts.</item>
    /// <item>For each translation, in the order given: its problems from its reader, then its blank texts, then the keys missing
    /// from it, then its extra keys, then for each key both files have (in ordinal order) the same text (unless allow-listed), the
    /// named placeholders and the numeric placeholders, and last the stale allow-list entries.</item>
    /// </list>
    /// <para>Blank texts are in file order. Every other group is in ordinal key order.</para>
    /// </remarks>
    public static IReadOnlyList<TranslationProblem> Check(
        TranslationMap baseMap,
        IEnumerable<TranslationMap> translations,
        TranslationParityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baseMap);
        ArgumentNullException.ThrowIfNull(translations);
        options ??= new TranslationParityOptions();
        var allowed = AllowList(options);

        List<TranslationProblem> problems = [];
        problems.AddRange(baseMap.Problems);
        AddBlankValues(baseMap, problems);

        foreach (var translation in translations)
        {
            ArgumentNullException.ThrowIfNull(translation);
            CheckTranslation(baseMap, translation, allowed, problems);
        }

        return problems.AsReadOnly();
    }

    private static HashSet<string> AllowList(TranslationParityOptions options)
    {
        if (options.SameTextAllowList is null)
        {
            throw new ArgumentException("The same-text allow-list is null. Use an empty list when no key is allowed.", nameof(options));
        }

        HashSet<string> allowed = new(StringComparer.Ordinal);
        foreach (var key in options.SameTextAllowList)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("The same-text allow-list has a blank entry. Remove it, or name its key.", nameof(options));
            }

            allowed.Add(key);
        }

        return allowed;
    }

    private static void CheckTranslation(
        TranslationMap baseMap,
        TranslationMap translation,
        HashSet<string> allowed,
        List<TranslationProblem> problems)
    {
        problems.AddRange(translation.Problems);
        AddBlankValues(translation, problems);

        foreach (var key in SortedKeys(baseMap))
        {
            if (!translation.ContainsKey(key))
            {
                problems.Add(new TranslationProblem(translation.Name, key, null, TranslationProblemKind.MissingFromTranslation,
                    $"\"{key}\" is in {baseMap.Name} but not in {translation.Name}."));
            }
        }

        foreach (var key in SortedKeys(translation))
        {
            if (!baseMap.ContainsKey(key))
            {
                problems.Add(new TranslationProblem(translation.Name, key, translation.EntryOf(key)?.Line, TranslationProblemKind.ExtraInTranslation,
                    $"\"{key}\" is in {translation.Name} but not in {baseMap.Name}."));
            }
        }

        foreach (var key in SortedKeys(baseMap))
        {
            var original = baseMap.EntryOf(key);
            var translated = translation.EntryOf(key);
            if (original is not null && translated is not null)
            {
                CheckSharedKey(baseMap, translation, original, translated, allowed, problems);
            }
        }

        foreach (var key in allowed.OrderBy(k => k, StringComparer.Ordinal))
        {
            CheckAllowListEntry(baseMap, translation, key, problems);
        }
    }

    private static void CheckSharedKey(
        TranslationMap baseMap,
        TranslationMap translation,
        TranslationEntry original,
        TranslationEntry translated,
        HashSet<string> allowed,
        List<TranslationProblem> problems)
    {
        var key = original.Key;
        var identical = string.Equals(original.Value, translated.Value, StringComparison.Ordinal) && !IsBlank(translated.Value);
        if (identical && !allowed.Contains(key))
        {
            problems.Add(new TranslationProblem(translation.Name, key, translated.Line, TranslationProblemKind.IdenticalText,
                $"\"{key}\" has the same text in {baseMap.Name} and {translation.Name} (\"{translated.Value}\"): translate it, "
                + "or add the key to the same-text allow-list with its reason."));
        }

        var namedBase = Placeholders.Named(original.Value);
        var namedTranslation = Placeholders.Named(translated.Value);
        if (!namedBase.SetEquals(namedTranslation))
        {
            problems.Add(new TranslationProblem(translation.Name, key, translated.Line, TranslationProblemKind.NamedPlaceholderMismatch,
                $"\"{key}\" has named placeholders {Placeholders.Describe(namedBase, numeric: false)} in {baseMap.Name} "
                + $"but {Placeholders.Describe(namedTranslation, numeric: false)} in {translation.Name}."));
        }

        var numericBase = Placeholders.Numeric(original.Value);
        var numericTranslation = Placeholders.Numeric(translated.Value);
        if (!numericBase.SetEquals(numericTranslation))
        {
            problems.Add(new TranslationProblem(translation.Name, key, translated.Line, TranslationProblemKind.NumericPlaceholderMismatch,
                $"\"{key}\" has numeric placeholders {Placeholders.Describe(numericBase, numeric: true)} in {baseMap.Name} "
                + $"but {Placeholders.Describe(numericTranslation, numeric: true)} in {translation.Name}."));
        }
    }

    private static void CheckAllowListEntry(
        TranslationMap baseMap,
        TranslationMap translation,
        string key,
        List<TranslationProblem> problems)
    {
        var original = baseMap.EntryOf(key);
        var translated = translation.EntryOf(key);
        if (original is null)
        {
            problems.Add(new TranslationProblem(translation.Name, key, null, TranslationProblemKind.StaleAllowListEntry,
                $"\"{key}\" is on the same-text allow-list but is not in {baseMap.Name}: remove it."));
        }
        else if (translated is null)
        {
            problems.Add(new TranslationProblem(translation.Name, key, null, TranslationProblemKind.StaleAllowListEntry,
                $"\"{key}\" is on the same-text allow-list but is not in {translation.Name}: remove it."));
        }
        else if (!string.Equals(original.Value, translated.Value, StringComparison.Ordinal))
        {
            problems.Add(new TranslationProblem(translation.Name, key, translated.Line, TranslationProblemKind.StaleAllowListEntry,
                $"\"{key}\" is on the same-text allow-list but its texts differ ({baseMap.Name} \"{original.Value}\", "
                + $"{translation.Name} \"{translated.Value}\"): remove it from the allow-list."));
        }
    }

    private static void AddBlankValues(TranslationMap map, List<TranslationProblem> problems)
    {
        foreach (var entry in map.Entries)
        {
            if (IsBlank(entry.Value))
            {
                problems.Add(new TranslationProblem(map.Name, entry.Key, entry.Line, TranslationProblemKind.BlankValue,
                    $"\"{entry.Key}\" has a blank value in {map.Name}."));
            }
        }
    }

    private static IEnumerable<string> SortedKeys(TranslationMap map) =>
        map.Entries.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal);

    private static bool IsBlank(string value) => string.IsNullOrWhiteSpace(value);
}
