// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Tests;

/// <summary>
/// The default log: every failure is traced with its detail, and only the source, summary, code, trace id and exception type are kept for a crash report.
/// </summary>
public sealed class ClientLogTests
{
    [Fact]
    public void A_failure_is_traced_with_its_code_trace_detail_and_exception()
    {
        var lines = new List<string>();
        var log = new ClientLog(write: lines.Add);

        log.Failure("Plan", "load failed", code: "HTTP_500", detail: "the detail", traceId: "t1", ex: new InvalidOperationException("the message"));

        var line = Assert.Single(lines);
        Assert.StartsWith("[Plan] load failed code=HTTP_500 traceId=t1 detail=the detail", line, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: the message", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_is_kept_without_its_detail_or_its_exception_message()
    {
        var log = new ClientLog(write: _ => { });

        log.Failure("Plan", "load failed", code: "HTTP_500", detail: "SECRET-DETAIL-45.8123456", traceId: "t1", ex: new InvalidOperationException("SECRET-MESSAGE rider@example.com"));

        var kept = Assert.Single(log.Recent.Snapshot());
        Assert.Equal("Plan", kept.Source);
        Assert.Equal("load failed", kept.Summary);
        Assert.Equal("HTTP_500", kept.Code);
        Assert.Equal("t1", kept.TraceId);
        Assert.Equal("System.InvalidOperationException", kept.ExceptionType);

        var everything = string.Join('|', kept.Source, kept.Summary, kept.Code, kept.TraceId, kept.ExceptionType);
        Assert.DoesNotContain("SECRET", everything, StringComparison.Ordinal);
    }

    [Fact]
    public void A_warning_is_traced_and_not_kept_for_a_report()
    {
        var lines = new List<string>();
        var log = new ClientLog(write: lines.Add);

        log.Warning("Plan", "slow");

        Assert.Equal("[Plan] WARNING slow", Assert.Single(lines));
        Assert.Equal(0, log.Recent.Count);
    }

    [Fact]
    public void Each_kept_failure_carries_the_time_of_the_clock_it_was_given()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));
        var log = new ClientLog(write: _ => { }, clock: clock);

        log.Failure("Plan", "load failed");

        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc), Assert.Single(log.Recent.Snapshot()).TimeUtc);
    }
}
