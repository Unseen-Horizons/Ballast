// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Text;

/// <summary>
/// Hides what a person typed that should not travel onward (into a search index, a log or a model): email addresses
/// become <c>[email]</c>, phone numbers become <c>[phone]</c>, and a link keeps only its host, as <c>[link: example.com]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Pure: the same text always gives the same result. Letters are never touched, so text in any script passes through
/// unchanged. Dates, times, line numbers, lists of numbers and prices are kept. A file path is not masked.
/// </para>
/// <para>
/// The masker finds the patterns it knows. It does not find an address or number written in words, or a link without a
/// scheme or <c>www.</c>, and it does not decide whether a match is personal. Read the usage guide for what is missed and
/// what is hidden by mistake.
/// </para>
/// </remarks>
public static partial class PersonalDataMasker
{
    private const string EmailPlaceholder = "[email]";
    private const string PhonePlaceholder = "[phone]";
    private const string LinkPlaceholder = "[link]";

    /// <summary>Characters that end a sentence or close a bracket, not a link: they are left after the placeholder.</summary>
    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':', '!', '?', ')', ']', '}', '>', '"', '\'', '»', '”', '’'];

    /// <summary>
    /// The text with its emails, phone numbers and links masked.
    /// </summary>
    /// <param name="text">The text to mask. Null or empty gives the empty string.</param>
    /// <returns>The masked text. Text with nothing to hide comes back equal to the input.</returns>
    /// <remarks>
    /// Never throws. The three rules run in order: links, then emails, then phone numbers.
    /// </remarks>
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var masked = LinkPattern().Replace(text, MaskLink);
        masked = EmailPattern().Replace(masked, EmailPlaceholder);
        return PhonePattern().Replace(masked, MaskPhone);
    }

    /// <summary>
    /// Whether <see cref="Mask(string?)"/> would change <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text to check. Null or empty is <c>false</c>.</param>
    /// <returns>
    /// <c>true</c> when the masked text differs from the input, by an ordinal comparison. Text that already contains the
    /// placeholders <c>[email]</c> or <c>[phone]</c> has nothing left to hide, so it is <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Use it to refuse a value (a user's pick, a free-text note) that would otherwise carry personal data onward.
    /// </remarks>
    public static bool ContainsPersonalData(string? text)
    {
        var original = text ?? string.Empty;
        return !string.Equals(Mask(original), original, StringComparison.Ordinal);
    }

    private static string MaskLink(Match match)
    {
        var value = match.Value;
        var core = value.TrimEnd(TrailingPunctuation);
        var trailing = value[core.Length..];
        if (core.Length == 0)
            return value;

        var candidate = core.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + core : core;
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            var host = uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
            return $"[link: {host}]{trailing}";
        }

        return LinkPlaceholder + trailing;
    }

    /// <summary>A phone number is masked; a date (2026-10-26, 26.10.2026, 26-10-2026) is kept.</summary>
    private static string MaskPhone(Match match)
    {
        var value = match.Value;
        return IsDate(value) ? value : PhonePlaceholder;
    }

    private static bool IsDate(string value) =>
        IsoDatePattern().IsMatch(value) || DayFirstDatePattern().IsMatch(value);

    [GeneratedRegex(@"(?i)(?:https?://|www\.)[^\s<>""']+", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"(?i)[a-z0-9._%+\-]+@[a-z0-9\-]+(?:\.[a-z0-9\-]+)*\.[a-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    /// <summary>
    /// The phone rule. Two forms, and a match must not touch a letter, a digit or a colon on either side, so <c>Line 6</c>
    /// and <c>14:05</c> stay:
    /// <list type="bullet">
    /// <item>a run of seven or more digits with no separator (with an optional leading <c>+</c>);</item>
    /// <item>a separated number that starts with <c>+</c>, <c>0</c> or <c>(0</c>, with eight or more digits in all, whose
    /// separators are only a space, <c>-</c>, <c>/</c> or brackets. A dot is never a phone separator, so times
    /// (<c>14.05</c>, <c>07.30 08.15</c>) and lists of numbers (<c>101 102 103</c>) are kept.</item>
    /// </list>
    /// </summary>
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:\+?\d{7,}|(?:\+|\(?(?=0))\(?\d(?:\)?[ \-/]?\(?\d){7,})(?![\p{L}\p{N}:])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^\d{4}[\-./]\d{1,2}[\-./]\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDatePattern();

    [GeneratedRegex(@"^\d{1,2}[\-./]\d{1,2}[\-./]\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex DayFirstDatePattern();
}
