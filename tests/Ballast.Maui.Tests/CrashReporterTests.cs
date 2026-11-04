// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Tests;

/// <summary>
/// The reporter writes one scrubbed report per exception, a newer one replaces an older unsent one, and recording never throws. Installing the process-wide hooks is not tested here: it would change every test in the process.
/// </summary>
public sealed class CrashReporterTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly FakeAppBuildInfo _info = new();
    private readonly ClientLog _log = new(write: _ => { });

    public void Dispose() => _root.Dispose();

    private CrashReportStore Store() => new(Path.Combine(_root.Path, "crash"));

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Recording_writes_a_report_that_names_the_exception_the_hook_and_the_build()
    {
        var store = Store();
        var reporter = new CrashReporter(store, _info, _log);

        Assert.True(reporter.Record(Thrown("boom"), "Test hook"));

        var text = store.ReadPending();
        Assert.NotNull(text);
        Assert.Contains("InvalidOperationException: boom", text, StringComparison.Ordinal);
        Assert.Contains("Caught by: Test hook", text, StringComparison.Ordinal);
        Assert.Contains("App: 0.9.0 (25)", text, StringComparison.Ordinal);
        Assert.Contains("Platform: Android 16", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_exception_arriving_through_two_hooks_is_written_once()
    {
        var store = Store();
        var reporter = new CrashReporter(store, _info, _log);
        var ex = Thrown("boom");

        Assert.True(reporter.Record(ex, "first hook"));
        Assert.True(store.Delete());

        Assert.True(reporter.Record(ex, "second hook"));
        Assert.False(store.HasPending);
    }

    [Fact]
    public void A_newer_crash_replaces_an_older_unsent_one()
    {
        var store = Store();
        var reporter = new CrashReporter(store, _info, _log);

        reporter.Record(Thrown("first"), "hook");
        reporter.Record(Thrown("second"), "hook");

        Assert.Contains("second", store.ReadPending()!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_platform_value_that_cannot_be_read_is_unknown_and_the_report_is_still_written()
    {
        var store = Store();
        _info.ReadFault = new InvalidOperationException("no platform");
        var reporter = new CrashReporter(store, _info, _log);

        Assert.True(reporter.Record(Thrown("boom"), "hook"));
        Assert.Contains("App: unknown (unknown)", store.ReadPending()!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failures_kept_so_far_ride_along_in_the_report()
    {
        var store = Store();
        _log.Failure("Plan", "load failed", code: "HTTP_500");
        var reporter = new CrashReporter(store, _info, _log);

        reporter.Record(Thrown("boom"), "hook");

        Assert.Contains("Plan: load failed", store.ReadPending()!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_with_no_exception_is_still_written()
    {
        var store = Store();
        var reporter = new CrashReporter(store, _info, _log);

        Assert.True(reporter.Record(null, "hook"));
        Assert.Contains("No exception was available.", store.ReadPending()!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_that_cannot_be_written_answers_false_and_never_throws()
    {
        // The store's folder path is taken by a file, so nothing can be saved there.
        var blocked = Path.Combine(_root.Path, "crash");
        File.WriteAllText(blocked, "in the way");
        var reporter = new CrashReporter(new CrashReportStore(blocked), _info, _log);

        Assert.False(reporter.Record(Thrown("boom"), "hook"));
    }
}
