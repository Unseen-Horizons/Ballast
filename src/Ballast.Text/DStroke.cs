// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Text;

/// <summary>
/// How the letter đ (and its capital Đ) folds in <see cref="TextFold.Fold(string?, DStroke)"/>. It has no accent to drop,
/// so a rule has to pick a letter for it.
/// </summary>
public enum DStroke
{
    /// <summary>The stroke folds to <c>d</c>: "Đakovo" becomes "dakovo". The default.</summary>
    D,

    /// <summary>The stroke folds to <c>dj</c>: "Đakovo" becomes "djakovo", as some sources write such names.</summary>
    Dj,
}
