// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Platform;

/// <summary>
/// The screen's brightness for a moment that needs it, such as a ticket's code being scanned: the brightness goes up, and comes back when the
/// moment ends. The change is the app's window only, never the system setting.
/// </summary>
public interface IScreenBrightness
{
    /// <summary>Raises the screen to full brightness. A second call before <see cref="Restore"/> keeps the value from the first.</summary>
    void SetFull();

    /// <summary>Puts back the brightness the window had before <see cref="SetFull"/>.</summary>
    void Restore();
}
