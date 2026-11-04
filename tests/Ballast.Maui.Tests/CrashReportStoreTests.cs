// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Tests;

/// <summary>The one waiting crash report: a newer one replaces an older one, and a write that cannot happen is reported, never thrown.</summary>
public sealed class CrashReportStoreTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly string _dir;

    public CrashReportStoreTests()
    {
        _dir = Path.Combine(_root.Path, "crash");
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public void With_nothing_saved_there_is_nothing_pending()
    {
        var store = new CrashReportStore(_dir);

        Assert.False(store.HasPending);
        Assert.Null(store.ReadPending());
    }

    [Fact]
    public void A_saved_report_is_pending_and_reads_back_exactly()
    {
        var store = new CrashReportStore(_dir);

        Assert.True(store.Save("Crash: X\nline two — ü"));

        Assert.True(store.HasPending);
        Assert.Equal("Crash: X\nline two — ü", store.ReadPending());
    }

    [Fact]
    public void A_newer_crash_replaces_an_older_unsent_one()
    {
        var store = new CrashReportStore(_dir);
        store.Save("first");

        store.Save("second");

        Assert.Equal("second", store.ReadPending());
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Deleting_removes_it_and_deleting_nothing_is_fine()
    {
        var store = new CrashReportStore(_dir);
        store.Save("x");

        Assert.True(store.Delete());
        Assert.False(store.HasPending);
        Assert.True(store.Delete());
    }

    [Fact]
    public void A_write_that_cannot_happen_returns_false_instead_of_throwing()
    {
        // The folder's path is taken by a file, so the folder cannot be made.
        File.WriteAllText(_dir, "in the way");
        var store = new CrashReportStore(_dir);

        Assert.False(store.Save("x"));
    }

    [Fact]
    public void A_half_written_temp_file_is_not_a_pending_report()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "pending-crash.tmp"), "half");
        var store = new CrashReportStore(_dir);

        Assert.False(store.HasPending);
        Assert.Null(store.ReadPending());
    }
}
