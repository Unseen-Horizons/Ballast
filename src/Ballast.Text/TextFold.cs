// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;

namespace Ballast.Text;

/// <summary>
/// Folds text into the form that two spellings of one name share. Accents, letter case, apostrophes, punctuation and the
/// amount of whitespace do not count, so "Trg bana Jelačića" and "trg bana jelacica" fold to the same text.
/// </summary>
/// <remarks>
/// <para>
/// The folded form is for comparing, never for showing. It is not a transliteration: letters outside the Latin alphabet
/// stay as they are (Cyrillic is kept, not written in Latin letters).
/// </para>
/// </remarks>
public static class TextFold
{
    /// <summary>
    /// Folds <paramref name="text"/> for comparison.
    /// </summary>
    /// <param name="text">The text to fold. Null or empty gives the empty string.</param>
    /// <param name="dStroke">How the letter đ folds: to <c>d</c> (the default) or to <c>dj</c>.</param>
    /// <returns>
    /// The folded text. Accents and combining marks are dropped. Letters are lower case, in the invariant culture. The
    /// letter <c>ß</c> becomes <c>ss</c>. Apostrophes (straight and typographic) are deleted, so <c>McDonald's</c> becomes
    /// <c>mcdonalds</c>. A run of whitespace and punctuation becomes one space, and there is none at either end.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Abbreviation points are not expanded: "J." folds to "j". Symbols are not separators, so "€" and "+" stay in the text.
    /// Hangul syllables are decomposed into their letters by the Unicode normalisation that comes first.
    /// </para>
    /// <para>
    /// Surrogate pairs are not lower-cased (letters outside the Basic Multilingual Plane keep their case).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dStroke"/> is not <see cref="DStroke.D"/> or <see cref="DStroke.Dj"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> contains an unpaired surrogate, which is not text. It is refused rather than replaced, so
    /// the caller can report the bad input.
    /// </exception>
    public static string Fold(string? text, DStroke dStroke = DStroke.D)
    {
        if (!Enum.IsDefined(dStroke))
            throw new ArgumentOutOfRangeException(nameof(dStroke), dStroke, "The stroke rule must be D or Dj.");
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Decompose, drop the combining marks (the caron on č, the acute on é), then fold the letters that have no
        // decomposition of their own.
        var letters = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;

            // Apostrophes are deleted, not turned into a separator: "McDonald's" is "mcdonalds", which is what people type.
            if (c is '\'' or '’' or '‘' or 'ʼ' or '`')
                continue;

            var lower = char.ToLowerInvariant(c);
            switch (lower)
            {
                case 'đ':
                    letters.Append(dStroke == DStroke.Dj ? "dj" : "d");
                    break;
                case 'ß':
                    letters.Append("ss");
                    break;
                default:
                    letters.Append(lower);
                    break;
            }
        }

        // Whitespace and punctuation separate words: a run of them becomes one space, and none leads or trails.
        var result = new StringBuilder(letters.Length);
        var pendingSeparator = false;
        foreach (var c in letters.ToString())
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                pendingSeparator = result.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                result.Append(' ');
                pendingSeparator = false;
            }

            result.Append(c);
        }

        return result.ToString();
    }
}
