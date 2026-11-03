// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations.Tests;

/// <summary>
/// A folder for one test to write into, beside the test assembly. It is removed when disposed.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    private TempFolder(string path)
    {
        Path = path;
    }

    /// <summary>The full path of the folder. It exists until the folder is disposed.</summary>
    public string Path { get; }

    /// <summary>Creates a new, empty folder.</summary>
    /// <returns>The folder.</returns>
    public static TempFolder Create()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "translations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempFolder(path);
    }

    /// <summary>Removes the folder and everything in it.</summary>
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
