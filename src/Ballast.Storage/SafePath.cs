// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;

namespace Ballast.Storage;

/// <summary>
/// Turns a relative key into a path under a root, and refuses any key that could name something else. A key comes from a
/// request or from a stored row, so it is treated as hostile. The rules are the strictest that any of the apps used, and they
/// are the same on every operating system, so a key that is safe on one system is safe on all of them.
/// </summary>
/// <remarks>
/// A key is one or more segments joined by <c>/</c>. Each segment must be a plain name: no <c>.</c> or <c>..</c>, no empty
/// segment, no reserved or control character, no white space at either end, no trailing dot, and not a Windows device name.
/// The whole key must have no <c>\</c>, no <c>:</c>, and must not start with <c>/</c>. The check is textual. A symbolic link
/// inside the root is not detected.
/// </remarks>
public static class SafePath
{
    private static readonly SearchValues<char> ReservedCharacters = SearchValues.Create("<>\"|?*");

    private static readonly string[] DeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>
    /// Resolves <paramref name="key"/> against <paramref name="root"/> and returns the full path. Nothing is created and nothing
    /// is read from the file system.
    /// </summary>
    /// <param name="root">The folder that every key must stay inside. A relative root is resolved against the working directory.</param>
    /// <param name="key">The relative key, with <c>/</c> between segments.</param>
    /// <returns>The full path of the key under the root.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="root"/> is null, empty or only white space, or it contains a character that a path cannot hold.
    /// </exception>
    /// <exception cref="SafePathException">The key is refused. <see cref="SafePathException.Reason"/> says why.</exception>
    public static string Resolve(string root, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new SafePathException(SafePathReason.Empty);
        }

        if (key.Contains('\\'))
        {
            throw new SafePathException(SafePathReason.Backslash);
        }

        if (key.Contains(':'))
        {
            throw new SafePathException(SafePathReason.Colon);
        }

        if (key[0] == '/')
        {
            throw new SafePathException(SafePathReason.RootedKey);
        }

        var segments = key.Split('/');
        foreach (var segment in segments)
        {
            CheckSegment(segment);
        }

        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var prefix = rootPath.EndsWith(Path.DirectorySeparatorChar) ? rootPath : rootPath + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(rootPath, Path.Combine(segments)));

        // Windows paths are not case sensitive, so the containment check must not be either.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, comparison))
        {
            throw new SafePathException(SafePathReason.OutsideRoot);
        }

        return full;
    }

    private static void CheckSegment(string segment)
    {
        if (segment.Length == 0)
        {
            throw new SafePathException(SafePathReason.EmptySegment);
        }

        if (segment is "." or "..")
        {
            throw new SafePathException(SafePathReason.DotSegment);
        }

        if (segment.AsSpan().IndexOfAny(ReservedCharacters) >= 0)
        {
            throw new SafePathException(SafePathReason.ReservedCharacter);
        }

        if (segment.Any(char.IsControl))
        {
            throw new SafePathException(SafePathReason.ControlCharacter);
        }

        if (segment != segment.Trim())
        {
            throw new SafePathException(SafePathReason.SurroundingSpace);
        }

        if (segment.EndsWith('.'))
        {
            throw new SafePathException(SafePathReason.TrailingDot);
        }

        if (IsDeviceName(segment))
        {
            throw new SafePathException(SafePathReason.ReservedName);
        }
    }

    /// <summary>A device name is reserved with or without an extension: <c>NUL</c> and <c>NUL.txt</c> both name the device.</summary>
    private static bool IsDeviceName(string segment)
    {
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? segment : segment[..dot];
        return Array.Exists(DeviceNames, name => string.Equals(name, stem, StringComparison.OrdinalIgnoreCase));
    }
}
