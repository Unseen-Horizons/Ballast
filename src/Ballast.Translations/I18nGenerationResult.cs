// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// What a generation run wrote and what it noticed.
/// </summary>
/// <param name="Cultures">The cultures, English first and the others in ordinal order.</param>
/// <param name="Files">The files written, as full paths, in the order they were written.</param>
/// <param name="Keys">The number of keys in each culture, in the order of <paramref name="Cultures"/>.</param>
/// <param name="StatusKeys">The number of status codes in the status map.</param>
/// <param name="Warnings">What the run noticed without stopping: an entry with no name, which is left out.</param>
public sealed record I18nGenerationResult(
    IReadOnlyList<string> Cultures,
    IReadOnlyList<string> Files,
    IReadOnlyList<int> Keys,
    int StatusKeys,
    IReadOnlyList<TranslationProblem> Warnings);
