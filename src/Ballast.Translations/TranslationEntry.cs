// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// One key and its text, as a file gave them.
/// </summary>
/// <param name="Key">The key. A key is compared exactly, letter case included.</param>
/// <param name="Value">The text. It is not trimmed: the checks see the text as the file holds it.</param>
/// <param name="Line">The line the entry starts on, when the file has lines (a .resx file does); otherwise null.</param>
public sealed record TranslationEntry(string Key, string Value, int? Line = null);
