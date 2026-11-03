// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Text;

using Ballast.Storage;

namespace Ballast.Maui.Diagnostics;

/// <summary>
/// The one crash report waiting to be offered: a single small text file in the app's own folder. A newer crash replaces an older unsent one,
/// and the file is removed only when the person answers (or the report is sent), never because a send failed.
/// </summary>
/// <remarks>
/// Written from a crash handler, so <see cref="Save"/> never throws: it reports whether the file is on disk and logs the reason when it is not.
/// The file is written beside its final name and moved over it (<see cref="AtomicFile"/>), so a process that dies mid-write leaves the previous
/// report or none, never half of one.
/// </remarks>
public sealed class CrashReportStore
{
    /// <summary>The name of the waiting report's file.</summary>
    public const string FileName = "pending-crash.txt";

    private readonly string _directory;

    /// <summary>Creates a store over one folder.</summary>
    /// <param name="directory">The folder the report is kept in. It is created when a report is saved.</param>
    public CrashReportStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    private string FilePath => Path.Combine(_directory, FileName);

    /// <summary>True when a report is waiting.</summary>
    public bool HasPending => File.Exists(FilePath);

    /// <summary>Stores <paramref name="text"/> as the waiting report, replacing any earlier one.</summary>
    /// <param name="text">The report text.</param>
    /// <returns>True when the report is on disk; false, with the reason traced, when it could not be written.</returns>
    public bool Save(string text)
    {
        try
        {
            AtomicFile.Write(FilePath, Encoding.UTF8.GetBytes(text), AtomicWriteMode.Overwrite);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.WriteLine($"[CrashReportStore] Could not save the crash report: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The waiting report, or null when there is none. An unreadable file is also null, and the cause is traced: it stays where it is, so a
    /// later start tries again rather than losing it.
    /// </summary>
    /// <returns>The report text, or null.</returns>
    public string? ReadPending()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[CrashReportStore] Could not read the waiting crash report: {ex.Message}");
            return null;
        }
    }

    /// <summary>Removes the waiting report.</summary>
    /// <returns>True when the file is gone afterwards; false, with the reason traced, when it is still there.</returns>
    public bool Delete()
    {
        try
        {
            File.Delete(FilePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[CrashReportStore] Could not delete the crash report: {ex.Message}");
            return false;
        }
    }
}
