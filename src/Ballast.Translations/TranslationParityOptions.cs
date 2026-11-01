// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// The options of <see cref="TranslationParity"/>. The defaults check everything, with an empty allow-list.
/// </summary>
public sealed record TranslationParityOptions
{
    /// <summary>
    /// The keys whose text may be the same in the base file and in the translation, on purpose. Use it for a word that a language
    /// writes the same way, such as a unit, a code or a language's name in its own language. Give the reason in a comment beside
    /// the key in the app, because the library keeps only the keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A key whose text is the same in both files and is not on this list is a problem (<see cref="TranslationProblemKind.IdenticalText"/>).
    /// A blank text is never reported this way, because the blank check reports it.
    /// </para>
    /// <para>
    /// An entry is stale, and is reported as <see cref="TranslationProblemKind.StaleAllowListEntry"/>, when its key is missing from
    /// the base file or from the translation, or when the two texts are no longer the same. A stale entry must be removed. Entries
    /// are compared exactly, letter case included. An entry that is null, empty or only white space makes the check throw
    /// <see cref="ArgumentException"/>.
    /// </para>
    /// </remarks>
    public IEnumerable<string> SameTextAllowList { get; init; } = [];
}
