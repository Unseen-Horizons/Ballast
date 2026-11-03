// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Helpers;

/// <summary>
/// Says "yes" once: the first time something is wanted, and never again. It is the decision behind a lazily built section of a page: the section is
/// created the first time it is needed and then kept, so hiding and showing it again does not rebuild it. Pure, so a test can pin it.
/// </summary>
public sealed class FirstUse
{
    private bool _taken;

    /// <summary>Answers whether this is the first time <paramref name="wanted"/> was true.</summary>
    /// <param name="wanted">Whether the thing is wanted now.</param>
    /// <returns>True when it is wanted and this is the first time it was; false otherwise, including after that.</returns>
    public bool Take(bool wanted)
    {
        if (!wanted || _taken)
            return false;

        _taken = true;
        return true;
    }

    /// <summary>Whether <see cref="Take"/> has ever said yes.</summary>
    public bool IsTaken => _taken;
}
