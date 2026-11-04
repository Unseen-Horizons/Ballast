// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Translations;

/// <summary>
/// Reads the placeholders of a text as sets: the named ones (<c>{name}</c>) and the numeric ones (<c>{0}</c>, <c>{1:N2}</c>). The
/// order of the placeholders and how many times each one appears do not matter, only which ones appear.
/// </summary>
internal static partial class Placeholders
{
    // A named placeholder starts with a letter or an underscore. A format (<c>{name:N2}</c>) or an alignment (<c>{name,10}</c>)
    // after the name is allowed, and is not part of the name.
    [GeneratedRegex(@"\{([A-Za-z_]\w*)(?:[,:][^}]*)?\}")]
    private static partial Regex NamedPattern();

    // A numeric placeholder is an index in ASCII digits, with the same optional format or alignment.
    [GeneratedRegex(@"\{([0-9]+)(?:[,:][^}]*)?\}")]
    private static partial Regex NumericPattern();

    /// <summary>
    /// The names of the named placeholders in a text.
    /// </summary>
    public static HashSet<string> Named(string text)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (Match match in NamedPattern().Matches(text))
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    /// <summary>
    /// The indexes of the numeric placeholders in a text. Leading zeros are dropped, so <c>{01}</c> and <c>{1}</c> are the same.
    /// </summary>
    public static HashSet<string> Numeric(string text)
    {
        HashSet<string> indexes = new(StringComparer.Ordinal);
        foreach (Match match in NumericPattern().Matches(text))
        {
            var digits = match.Groups[1].Value.TrimStart('0');
            indexes.Add(digits.Length == 0 ? "0" : digits);
        }

        return indexes;
    }

    /// <summary>
    /// Writes a set of placeholders for a message: <c>{a} {b}</c>, or <c>none</c> for an empty set. Numeric indexes are put in
    /// numeric order.
    /// </summary>
    public static string Describe(IEnumerable<string> placeholders, bool numeric)
    {
        var ordered = numeric
            ? placeholders.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal)
            : placeholders.OrderBy(p => p, StringComparer.Ordinal);
        var shown = ordered.Select(p => "{" + p + "}").ToList();
        return shown.Count == 0 ? "none" : string.Join(" ", shown);
    }
}
