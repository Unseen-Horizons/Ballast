// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Testing.Guards;

/// <summary>Finds the root of a repository from a file or folder that only the root holds.</summary>
public static class RepoRoot
{
    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> (by default the folder the test assembly runs from) and returns the first
    /// folder that holds <paramref name="marker"/>. A guard names a marker that only its repository has, such as <c>Ballast.slnx</c>,
    /// so a guard run from a copy of the code finds that copy's root and never another repository's.
    /// </summary>
    /// <param name="marker">A file or folder name, relative to the root, such as <c>Ballast.slnx</c>. Required, not blank.</param>
    /// <param name="startDirectory">Where the walk begins. Default: <see cref="AppContext.BaseDirectory"/>.</param>
    /// <exception cref="DirectoryNotFoundException">No folder from the start up to the drive root holds the marker.</exception>
    public static string Find(string marker, string? startDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, marker);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root: no folder from '{startDirectory ?? AppContext.BaseDirectory}' up holds '{marker}'.");
    }
}
