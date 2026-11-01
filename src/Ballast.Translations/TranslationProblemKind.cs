// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// What a <see cref="TranslationProblem"/> is about. The names are part of the library's contract: a test that filters on one
/// of them keeps working, and a new kind is only ever added at the end.
/// </summary>
public enum TranslationProblemKind
{
    /// <summary>The key is in the file more than once. The first value is kept and the later entry is not read.</summary>
    DuplicateKey,

    /// <summary>An entry has no name, so it is not read.</summary>
    UnnamedEntry,

    /// <summary>A JSON value is not a string (a number, a boolean, null or an array), so it is not read.</summary>
    UnsupportedValue,

    /// <summary>The key is in the base file but not in the translation.</summary>
    MissingFromTranslation,

    /// <summary>The key is in the translation but not in the base file.</summary>
    ExtraInTranslation,

    /// <summary>The text is empty or only white space.</summary>
    BlankValue,

    /// <summary>The named placeholders (<c>{name}</c>) of the text differ between the base file and the translation.</summary>
    NamedPlaceholderMismatch,

    /// <summary>The numeric placeholders (<c>{0}</c>, <c>{1:N2}</c>) of the text differ between the base file and the translation.</summary>
    NumericPlaceholderMismatch,

    /// <summary>
    /// The translation has the same text as the base file, and the key is not on the same-text allow-list.
    /// </summary>
    IdenticalText,

    /// <summary>
    /// An allow-list entry names a key that is missing from one of the files, or whose texts are no longer the same. The entry
    /// is stale and should be removed.
    /// </summary>
    StaleAllowListEntry,

    /// <summary>A key that a scan found in source text is not in the base map.</summary>
    UnknownKey,
}
