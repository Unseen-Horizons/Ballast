// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Tests;

/// <summary>The last handled failures a crash report carries: how many, in what order, and what is never kept.</summary>
public sealed class RecentFailureLogTests
{
    private static RecentFailure Failure(int n) =>
        RecentFailure.Create(new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc).AddSeconds(n), "Source" + n, "summary " + n, null, null, null);

    [Fact]
    public void It_keeps_the_last_thirty_and_drops_the_oldest_first()
    {
        var log = new RecentFailureLog();
        for (var i = 1; i <= 45; i++)
            log.Add(Failure(i));

        var kept = log.Snapshot();

        Assert.Equal(30, RecentFailureLog.Capacity);
        Assert.Equal(30, kept.Count);
        Assert.Equal("Source16", kept[0].Source);
        Assert.Equal("Source45", kept[^1].Source);
    }

    [Fact]
    public void A_snapshot_is_a_copy_that_later_failures_do_not_change()
    {
        var log = new RecentFailureLog();
        log.Add(Failure(1));
        var snapshot = log.Snapshot();

        log.Add(Failure(2));

        Assert.Single(snapshot);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void Failures_added_from_many_threads_are_all_counted_up_to_the_capacity()
    {
        var log = new RecentFailureLog();

        Parallel.For(0, 200, i => log.Add(Failure(i)));

        Assert.Equal(RecentFailureLog.Capacity, log.Snapshot().Count);
    }

    [Fact]
    public void A_failure_is_scrubbed_and_capped_when_it_is_made()
    {
        var failure = RecentFailure.Create(
            DateTime.UtcNow,
            "PlanClient",
            "plan to https://motis.example.org/api?fromPlace=45.8123456,15.9765432 failed " + new string('x', 500),
            "HTTP_500",
            "0af7651916cd43dd8448eb211c80319c",
            "System.Net.Http.HttpRequestException");

        Assert.DoesNotContain("45.81", failure.Summary, StringComparison.Ordinal);
        Assert.Contains("[link: motis.example.org]", failure.Summary, StringComparison.Ordinal);
        Assert.True(failure.Summary.Length <= RecentFailure.MaxSummaryLength);
        Assert.Equal("HTTP_500", failure.Code);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", failure.TraceId);
    }

    [Fact]
    public void A_blank_code_or_trace_is_kept_as_none()
    {
        var failure = RecentFailure.Create(DateTime.UtcNow, "S", "s", "  ", string.Empty, null);

        Assert.Null(failure.Code);
        Assert.Null(failure.TraceId);
        Assert.Null(failure.ExceptionType);
    }
}
