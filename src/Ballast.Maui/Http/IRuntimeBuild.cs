// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Http;

/// <summary>
/// Whether this installed build is a debuggable one. The library asks, instead of reading a compile-time symbol, so a
/// release build that a developer tries to make insecure is refused at run time, not trusted because of how it was compiled.
/// </summary>
public interface IRuntimeBuild
{
    /// <summary>
    /// True for a debuggable build, false for a release build, and null when the platform does not report it. A null is never
    /// read as either; it is treated as not refused, and the platform implementations say which platforms report it.
    /// </summary>
    bool? IsDebuggable { get; }
}
