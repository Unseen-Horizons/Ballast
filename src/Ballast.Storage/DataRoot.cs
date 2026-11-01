// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>
/// The one folder that an application writes its data to, apart from its own content. On a host whose content folder is
/// read-only, such as a package deploy, the data folder is named in configuration, and it is the only place the application
/// writes.
/// </summary>
/// <remarks>
/// A <see cref="DataRoot"/> holds a path and nothing else. The constructor is private, so <see cref="Resolve"/> is the only way
/// to make one. Outside development it refuses a blank setting, a folder that does not exist and a folder without its marker
/// file, before anything is written. It never creates the folder, because creating it would hide a wrong path.
/// </remarks>
public sealed class DataRoot
{
    private DataRoot(string fullPath, string markerFileName)
    {
        FullPath = fullPath;
        MarkerFileName = markerFileName;
    }

    /// <summary>The absolute path of the data folder.</summary>
    public string FullPath { get; }

    /// <summary>The name of the marker file that says a folder is the right data folder.</summary>
    public string MarkerFileName { get; }

    /// <summary>Where the marker file is expected: <see cref="FullPath"/> with <see cref="MarkerFileName"/> added.</summary>
    public string MarkerPath => Path.Combine(FullPath, MarkerFileName);

    /// <summary>True when the marker file exists. A directory with that name does not count.</summary>
    public bool MarkerExists() => File.Exists(MarkerPath);

    /// <summary>
    /// Resolves the data folder at startup. In development the configured path is used as it is, or <paramref name="defaultPath"/>
    /// when nothing is configured, and nothing is checked or created. Anywhere else the setting is required, and must name an
    /// existing folder that carries the marker file.
    /// </summary>
    /// <param name="configured">The configured folder, or null or blank when none is set.</param>
    /// <param name="defaultPath">The folder used in development when nothing is configured. Typically the content root.</param>
    /// <param name="isDevelopment">True in the development environment. Its checks are relaxed, and the default is allowed.</param>
    /// <param name="settingName">The name of the setting, as an operator writes it. It appears in every message.</param>
    /// <param name="markerFileName">The plain file name that marks a correct data folder, such as <c>.data-root</c>.</param>
    /// <returns>The data folder.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="settingName"/>, <paramref name="defaultPath"/> or <paramref name="markerFileName"/> is blank, or the marker
    /// name contains a folder separator.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Outside development: the setting is blank, the folder does not exist, or the marker file is absent. The message names the
    /// setting and the path, and says what to do.
    /// </exception>
    public static DataRoot Resolve(string? configured, string defaultPath, bool isDevelopment, string settingName, string markerFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        ArgumentException.ThrowIfNullOrWhiteSpace(markerFileName);
        if (Path.GetFileName(markerFileName) != markerFileName)
        {
            throw new ArgumentException("The marker file name is a plain file name, without a folder.", nameof(markerFileName));
        }

        if (isDevelopment)
        {
            var local = string.IsNullOrWhiteSpace(configured) ? defaultPath : configured;
            return new DataRoot(Path.GetFullPath(local), markerFileName);
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{settingName} is not set, so the application refuses to start. Outside development the content folder is normally " +
                $"read-only, so the writable data folder must be named. Set {settingName} to the data folder, which must contain " +
                $"the {markerFileName} marker file.");
        }

        var fullPath = Path.GetFullPath(configured);
        if (!Directory.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"{settingName} is '{fullPath}', which does not exist, so the application refuses to start. The folder is not " +
                "created automatically: a wrong path would then look right. Create the folder and its marker file, or correct the setting.");
        }

        var root = new DataRoot(fullPath, markerFileName);
        if (!root.MarkerExists())
        {
            throw new InvalidOperationException(
                $"{settingName} is '{fullPath}', which has no {markerFileName} marker file, so the application refuses to start: " +
                "this looks like a wrong or empty data folder. Create the marker only once the folder is confirmed as the right one.");
        }

        return root;
    }
}
