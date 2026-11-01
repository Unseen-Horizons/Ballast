// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// The result of a literal-key scan.
/// </summary>
/// <param name="Uses">The number of key uses found in the sources, whether or not the key is in the base map. A caller that
/// expects a minimum number of uses checks this, so a broken pattern that finds nothing cannot pass.</param>
/// <param name="Problems">A problem for each use whose key is not in the base map, in the order the sources were scanned.</param>
public sealed record LiteralKeyScanResult(int Uses, IReadOnlyList<TranslationProblem> Problems);
