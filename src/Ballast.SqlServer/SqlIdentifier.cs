// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.SqlServer;

/// <summary>
/// Checks a name that is about to be written into SQL text, and quotes it. A name that is not a plain identifier is refused, never
/// escaped and sent: the name is part of the statement, so it must not be able to change it.
/// </summary>
internal static class SqlIdentifier
{
    /// <summary>SQL Server's limit for an identifier, in characters.</summary>
    private const int MaxLength = 128;

    /// <summary>
    /// Checks a name of one or two parts (<c>name</c> or <c>schema.name</c>) and returns it bracket-quoted part by part, for example
    /// <c>[Audit].[Numbers]</c>. Each part starts with an ASCII letter or underscore and continues with ASCII letters, digits or
    /// underscores, and is at most 128 characters long.
    /// </summary>
    /// <param name="name">The name to check. Required.</param>
    /// <param name="parameterName">The name of the caller's parameter, for the exception.</param>
    /// <returns>The quoted name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">The name is empty, has more than two parts, or has a part that is not a plain identifier.</exception>
    internal static string QuoteQualifiedName(string name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);

        var parts = name.Split('.');
        if (parts.Length is < 1 or > 2 || parts.Any(part => !IsPlainIdentifier(part)))
        {
            throw new ArgumentException(
                "A SQL name must be a plain identifier, or a schema and an identifier separated by one dot. "
                    + "Letters, digits and underscores only; the first character is not a digit.",
                parameterName);
        }

        return string.Join('.', parts.Select(static part => "[" + part + "]"));
    }

    private static bool IsPlainIdentifier(string part)
    {
        if (part.Length is 0 or > MaxLength)
        {
            return false;
        }

        for (var i = 0; i < part.Length; i++)
        {
            var c = part[i];
            var valid = i == 0
                ? c == '_' || char.IsAsciiLetter(c)
                : c == '_' || char.IsAsciiLetterOrDigit(c);
            if (!valid)
            {
                return false;
            }
        }
        return true;
    }
}
