// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>Why <see cref="SafePath.Resolve"/> refused a key. A caller maps each reason to its own error code.</summary>
public enum SafePathReason
{
    /// <summary>The key is null, empty or only white space.</summary>
    Empty,

    /// <summary>The key contains a backslash. A key separates its segments with <c>/</c> only.</summary>
    Backslash,

    /// <summary>The key contains a colon, which names a drive or an alternate data stream on Windows.</summary>
    Colon,

    /// <summary>The key starts with <c>/</c>, so it is rooted rather than relative to the root.</summary>
    RootedKey,

    /// <summary>A segment is empty: two slashes in a row, or a slash at the end.</summary>
    EmptySegment,

    /// <summary>A segment is <c>.</c> or <c>..</c>.</summary>
    DotSegment,

    /// <summary>A segment contains one of <c>&lt; &gt; " | ? *</c>, which file names may not hold on Windows.</summary>
    ReservedCharacter,

    /// <summary>A segment contains a control character, such as a tab or a NUL.</summary>
    ControlCharacter,

    /// <summary>A segment starts or ends with white space.</summary>
    SurroundingSpace,

    /// <summary>A segment ends with a dot, which Windows silently removes from a name.</summary>
    TrailingDot,

    /// <summary>
    /// A segment is a Windows device name (<c>CON</c>, <c>PRN</c>, <c>AUX</c>, <c>NUL</c>, <c>COM1</c> to <c>COM9</c>,
    /// <c>LPT1</c> to <c>LPT9</c>), with or without an extension. It is refused on every system, so a key means the same
    /// thing wherever it is used.
    /// </summary>
    ReservedName,

    /// <summary>
    /// The resolved path is not inside the root. Every check above keeps a key inside the root, so this is the final guard
    /// and is not reached by a key that passes the others.
    /// </summary>
    OutsideRoot
}
