// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// One thing the checks found wrong. It names the file, the key when the problem is about one key, the line when the file has
/// lines, the kind, and a sentence a person can read.
/// </summary>
/// <param name="File">The file the problem is in: the name a map was given, or the source file of a scan.</param>
/// <param name="Key">The key the problem is about, or null when it is not about one key.</param>
/// <param name="Line">The line in the file, or null when the file has no lines or the problem is not on one line.</param>
/// <param name="Kind">What the problem is about.</param>
/// <param name="Message">A sentence that says what is wrong.</param>
public sealed record TranslationProblem(string File, string? Key, int? Line, TranslationProblemKind Kind, string Message)
{
    /// <summary>
    /// The problem on one line: the file, the line when there is one, and the message. Test output shows this text.
    /// </summary>
    /// <returns>The file, the line (when known) and the message, for example <c>SharedResources.resx:12: "save" has a blank value.</c>.</returns>
    public override string ToString() => Line is int line ? $"{File}:{line}: {Message}" : $"{File}: {Message}";
}
