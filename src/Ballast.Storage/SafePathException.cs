// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>
/// The refusal that <see cref="SafePath.Resolve"/> raises for a key that could leave its root, or that a file system would not
/// hold. It is an <see cref="ArgumentException"/> on the parameter <c>key</c>. The message names the reason and not the key, so a
/// log never receives a name that a request chose.
/// </summary>
public sealed class SafePathException : ArgumentException
{
    /// <summary>Creates the refusal for a key that failed <paramref name="reason"/>.</summary>
    /// <param name="reason">Why the key was refused.</param>
    public SafePathException(SafePathReason reason)
        : base(Describe(reason), "key")
    {
        Reason = reason;
    }

    /// <summary>Why the key was refused.</summary>
    public SafePathReason Reason { get; }

    private static string Describe(SafePathReason reason) => reason switch
    {
        SafePathReason.Empty => "The key is empty or only white space.",
        SafePathReason.Backslash => "The key contains a backslash. A key separates its segments with '/' only.",
        SafePathReason.Colon => "The key contains a colon, which names a drive or an alternate stream.",
        SafePathReason.RootedKey => "The key starts with '/'. A key is relative to the root.",
        SafePathReason.EmptySegment => "The key has an empty segment: two slashes in a row, or a slash at the end.",
        SafePathReason.DotSegment => "The key has a '.' or '..' segment.",
        SafePathReason.ReservedCharacter => "A segment of the key contains a reserved character: < > \" | ? *",
        SafePathReason.ControlCharacter => "A segment of the key contains a control character.",
        SafePathReason.SurroundingSpace => "A segment of the key starts or ends with white space.",
        SafePathReason.TrailingDot => "A segment of the key ends with a dot.",
        SafePathReason.ReservedName => "A segment of the key is a reserved device name (CON, PRN, AUX, NUL, COM1-COM9, LPT1-LPT9).",
        SafePathReason.OutsideRoot => "The key resolves to a path outside the root.",
        _ => "The key is not a safe relative path."
    };
}
