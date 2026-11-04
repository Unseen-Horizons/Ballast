// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;

namespace Ballast.Translations;

/// <summary>
/// The text rules the generator shares with the Node script it replaces: the trim, and the JSON text that
/// <c>JSON.stringify(value, null, 2)</c> writes. A generated file is compared byte for byte with the one the script wrote, so these
/// follow the JavaScript rules, not the .NET ones.
/// </summary>
internal static class JsText
{
    /// <summary>
    /// Removes the white space and the line breaks at both ends, with the set JavaScript's <c>trim()</c> removes. The .NET trim
    /// removes a different set: it keeps the byte order mark and removes the next-line character, so it is not used.
    /// </summary>
    /// <param name="text">The text. Required.</param>
    /// <returns>The text without its outer white space.</returns>
    public static string Trim(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return text.Substring(start, end - start);
    }

    /// <summary>
    /// Writes a JSON string, escaped as <c>JSON.stringify</c> escapes it: the quote, the backslash, the common control escapes, and every
    /// other control character and every lone surrogate as a lower-case <c>\uXXXX</c>. Everything else is written as it is.
    /// </summary>
    /// <param name="builder">Where the text goes.</param>
    /// <param name="value">The text. Required.</param>
    public static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        AppendEscape(builder, c);
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        builder.Append(c).Append(value[i + 1]);
                        i++;
                    }
                    else if (char.IsSurrogate(c))
                    {
                        AppendEscape(builder, c);
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    /// <summary>
    /// Writes an object of text values as <c>JSON.stringify(value, null, 2)</c> does: two spaces of indent, one property to a line, and
    /// <c>{}</c> for no properties. The properties come in the order JavaScript gives them (see <see cref="OrderedStrings.InJsOrder"/>).
    /// </summary>
    /// <param name="builder">Where the text goes.</param>
    /// <param name="properties">The properties, in the order they will be written. Required.</param>
    public static void WriteObject(StringBuilder builder, IEnumerable<KeyValuePair<string, string>> properties)
    {
        var first = true;
        foreach (var property in properties)
        {
            builder.Append(first ? "{\n" : ",\n");
            builder.Append("  ");
            WriteString(builder, property.Key);
            builder.Append(": ");
            WriteString(builder, property.Value);
            first = false;
        }

        builder.Append(first ? "{}" : "\n}");
    }

    /// <summary>
    /// Writes an array of text values as <c>JSON.stringify(value)</c> does: no spaces at all, and <c>[]</c> for no items.
    /// </summary>
    /// <param name="builder">Where the text goes.</param>
    /// <param name="items">The items, in order. Required.</param>
    public static void WriteArray(StringBuilder builder, IEnumerable<string> items)
    {
        builder.Append('[');
        var first = true;
        foreach (var item in items)
        {
            if (!first)
            {
                builder.Append(',');
            }

            WriteString(builder, item);
            first = false;
        }

        builder.Append(']');
    }

    /// <summary>
    /// True when a property name is an array index in JavaScript's sense: a canonical whole number below 4294967295. JavaScript lists
    /// those names first, in ascending order, before the others.
    /// </summary>
    /// <param name="key">The property name. Required.</param>
    /// <param name="index">The number, when the name is an array index.</param>
    /// <returns>True for an array index.</returns>
    public static bool IsArrayIndex(string key, out uint index)
    {
        index = 0;
        if (key.Length == 0 || key.Length > 10 || (key.Length > 1 && key[0] == '0'))
        {
            return false;
        }

        ulong value = 0;
        foreach (var c in key)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }

            value = (value * 10) + (ulong)(c - '0');
        }

        if (value >= 4294967295UL)
        {
            return false;
        }

        index = (uint)value;
        return true;
    }

    private static void AppendEscape(StringBuilder builder, char c)
    {
        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
    }

    private static bool IsWhiteSpace(char c)
    {
        var code = (int)c;
        return code switch
        {
            0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x20 or 0xA0 or 0x1680 or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 or 0xFEFF => true,
            >= 0x2000 and <= 0x200A => true,
            _ => false,
        };
    }
}
