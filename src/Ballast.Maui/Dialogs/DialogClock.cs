// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;

namespace Ballast.Maui.Dialogs;

/// <summary>
/// When a dialog last came up over the app (the crash offer, the permission flow, the store rating prompt). The rating prompt keeps its
/// distance from them. It is kept in memory on purpose: it only has to cover this run.
/// </summary>
public static class DialogClock
{
    private static long _lastTicks;

    /// <summary>Call as a dialog is shown.</summary>
    public static void Mark() => Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);

    /// <summary>The instant the last dialog was shown, in UTC, or null when none has been shown in this run.</summary>
    public static DateTime? LastUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }
}
