// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Storage;

/// <summary>What one sweep of stray temporary files did.</summary>
/// <param name="Removed">The full paths of the files removed.</param>
/// <param name="TooYoung">How many matching files were left because they were written too recently to be sure no writer holds them.</param>
/// <param name="Failed">The full paths of the matching files that could not be removed. Each is logged.</param>
public sealed record StrayTempSweepResult(IReadOnlyList<string> Removed, int TooYoung, IReadOnlyList<string> Failed);

/// <summary>
/// Removes the fixed-name temporary files that earlier writers left behind, once per install.
/// </summary>
/// <remarks>
/// Older writers named their temporary file after its target, with a fixed suffix (for example <c>imported.bin.tmp</c>). A crash
/// between the write and the move left it behind, and no later write reused it. The current writers use <see cref="Ballast.Storage.AtomicFile"/>,
/// whose temporary names are unique and end in <c>.atomic-tmp</c>, which never matches the old suffix. So a sweep of the old
/// suffix removes only the leftovers.
/// <para>
/// A file written in the last few minutes may still belong to another copy of the app, which a deploy can run side by side with
/// this one. Such a file is left alone. The one-time marker is set only when nothing young remains and nothing could not be removed,
/// so a sweep that was not complete runs again at the next start.
/// </para>
/// </remarks>
public static class StrayTempFiles
{
    /// <summary>The default age a stray file must reach before it is removed.</summary>
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Removes the files under each folder, searched recursively, whose name ends with <paramref name="suffix"/> and that are at
    /// least <paramref name="minimumAge"/> old. A folder that does not exist has nothing to sweep.
    /// </summary>
    /// <param name="folders">The folders to search, each recursively.</param>
    /// <param name="suffix">The suffix to match, with its dot, for example <c>.tmp</c>.</param>
    /// <param name="minimumAge">How old a file must be before it is removed.</param>
    /// <param name="log">Where a removal that fails is logged.</param>
    /// <returns>What was removed, what was too young, and what could not be removed.</returns>
    /// <exception cref="ArgumentException"><paramref name="suffix"/> is blank, or does not start with a dot.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumAge"/> is negative.</exception>
    /// <exception cref="IOException">A folder exists but cannot be listed.</exception>
    /// <exception cref="UnauthorizedAccessException">A folder exists but the app may not list it.</exception>
    public static StrayTempSweepResult Sweep(IEnumerable<string> folders, string suffix, TimeSpan minimumAge, IClientLog log)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        if (!suffix.StartsWith('.'))
            throw new ArgumentException("The suffix starts with a dot, for example .tmp.", nameof(suffix));
        if (minimumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumAge), minimumAge, "The minimum age must not be negative.");

        var removed = new List<string>();
        var failed = new List<string>();
        var tooYoung = 0;
        var cutoff = DateTime.UtcNow - minimumAge;

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            // Listed first: the folder is changed while it is swept, and a lazy listing is not a snapshot.
            foreach (var file in Directory.GetFiles(folder, "*" + suffix, SearchOption.AllDirectories))
            {
                if (!file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff)
                    {
                        tooYoung++;
                        continue;
                    }

                    File.Delete(file);
                    removed.Add(file);
                }
                catch (FileNotFoundException)
                {
                    // Gone since the listing: nothing is left to report.
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed.Add(file);
                    log.Warning(nameof(StrayTempFiles), $"could not remove a stray temporary file ({ex.GetType().Name})");
                }
            }
        }

        return new StrayTempSweepResult(removed, tooYoung, failed);
    }

    /// <summary>
    /// Runs <see cref="Sweep"/> unless the marker says it already completed on this install. The marker is a preference, so it
    /// survives a restart and is cleared with the app's data.
    /// </summary>
    /// <param name="folders">The folders to search.</param>
    /// <param name="suffix">The suffix to match, for example <c>.tmp</c>.</param>
    /// <param name="preferences">Where the marker is kept.</param>
    /// <param name="markerKey">The preference's key. Each app names its own.</param>
    /// <param name="sharedName">The preferences container of the marker, or null for the default one.</param>
    /// <param name="log">Where a removal that fails is logged.</param>
    /// <param name="minimumAge">How old a file must be before it is removed.</param>
    /// <returns>The sweep's result, or null when the marker says the sweep already completed.</returns>
    /// <exception cref="ArgumentException"><paramref name="markerKey"/> is blank.</exception>
    public static StrayTempSweepResult? RunOnce(
        IEnumerable<string> folders,
        string suffix,
        IPreferenceStore preferences,
        string markerKey,
        string? sharedName,
        IClientLog log,
        TimeSpan minimumAge)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentException.ThrowIfNullOrWhiteSpace(markerKey);

        if (preferences.Get(markerKey, false, sharedName))
            return null;

        var result = Sweep(folders, suffix, minimumAge, log);
        if (result.TooYoung == 0 && result.Failed.Count == 0)
            preferences.Set(markerKey, true, sharedName);

        return result;
    }
}
