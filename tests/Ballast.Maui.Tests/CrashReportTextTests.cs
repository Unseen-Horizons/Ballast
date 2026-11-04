// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Tests;

/// <summary>
/// The text of a crash report: what it holds, that it always fits the feedback message, and that nothing which could place the person (a request address with a position, an email, a path with an id in it) is left in it.
/// </summary>
public sealed class CrashReportTextTests
{
    private static readonly DateTime T0 = new(2026, 10, 7, 12, 34, 56, DateTimeKind.Utc);

    /// <summary>An exception that was really thrown, so it has a stack trace.</summary>
    private static Exception Thrown(string message, Exception? inner = null)
    {
        try
        {
            throw inner is null ? new InvalidOperationException(message) : new InvalidOperationException(message, inner);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static CrashReportInput Input(Exception? ex, IReadOnlyList<RecentFailure>? recent = null) =>
        new(T0, "1.4.2", "57", "Android", "14", "AppDomain.UnhandledException", ex, recent ?? []);

    private static RecentFailure Failure(int minute, string source, string summary = "load failed") =>
        RecentFailure.Create(T0.AddMinutes(-minute), source, summary, "ERR", "trace-" + minute, "System.IO.IOException");

    // ---- What a report holds ----------------------------------------------------------------

    [Fact]
    public void A_report_names_the_exception_the_build_the_system_and_the_time()
    {
        var text = CrashReportText.Compose(Input(Thrown("the thing broke")));

        Assert.StartsWith("Crash: System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: the thing broke", text, StringComparison.Ordinal);
        Assert.Contains("App: 1.4.2 (57)", text, StringComparison.Ordinal);
        Assert.Contains("Platform: Android 14", text, StringComparison.Ordinal);
        Assert.Contains("Time (UTC): 2026-10-07 12:34:56", text, StringComparison.Ordinal);
        Assert.Contains("Caught by: AppDomain.UnhandledException", text, StringComparison.Ordinal);
        Assert.Contains("at Ballast.Maui.Tests.CrashReportTextTests.Thrown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inner_exceptions_are_included()
    {
        var text = CrashReportText.Compose(Input(Thrown("outer", Thrown("the root cause"))));

        Assert.Contains("InvalidOperationException: outer", text, StringComparison.Ordinal);
        Assert.Contains("Caused by System.InvalidOperationException: the root cause", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_lists_each_of_its_children()
    {
        var aggregate = new AggregateException("many", Thrown("first child"), Thrown("second child"));

        var text = CrashReportText.Compose(Input(aggregate));

        Assert.Contains("first child", text, StringComparison.Ordinal);
        Assert.Contains("second child", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Recent_handled_failures_ride_along_oldest_first()
    {
        var text = CrashReportText.Compose(Input(Thrown("x"), [Failure(10, "OldSource"), Failure(1, "NewSource")]));

        Assert.Contains("Recent handled failures (oldest first, 2 of 2)", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("OldSource", StringComparison.Ordinal) < text.IndexOf("NewSource", StringComparison.Ordinal));
        Assert.Contains("code=ERR trace=trace-10 type=System.IO.IOException", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_with_no_exception_says_so()
    {
        var text = CrashReportText.Compose(Input(null));

        Assert.StartsWith("Crash: unknown", text, StringComparison.Ordinal);
        Assert.Contains("No exception was available.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_subject_is_the_first_line_and_is_cut_to_the_limit()
    {
        var text = CrashReportText.Compose(Input(Thrown("x")));

        Assert.Equal("Crash: System.InvalidOperationException", CrashReportText.SubjectFor(text));
        Assert.True(CrashReportText.SubjectFor(new string('a', 500)).Length <= CrashReportText.MaxSubjectLength);
    }

    // ---- Fitting the feedback message --------------------------------------------------------

    [Fact]
    public void A_huge_message_and_a_full_failure_log_still_fit()
    {
        var failures = Enumerable.Range(0, RecentFailureLog.Capacity)
            .Select(i => RecentFailure.Create(T0.AddMinutes(-i), "Source" + i, new string('x', 100), "ERR", "t", "System.IO.IOException"))
            .ToList();

        var text = CrashReportText.Compose(Input(Thrown(new string('m', 100_000), Thrown(new string('n', 100_000))), failures));

        Assert.True(text.Length <= CrashReportText.MaxLength, $"{text.Length} characters");
        Assert.StartsWith("Crash: ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void When_it_does_not_fit_the_oldest_failures_go_before_any_stack_frame()
    {
        var failures = Enumerable.Range(0, RecentFailureLog.Capacity)
            .Select(i => RecentFailure.Create(T0.AddMinutes(-(RecentFailureLog.Capacity - i)), "Source" + i, new string('x', 100), "ERR", "t", "System.IO.IOException"))
            .ToList();

        var text = CrashReportText.Compose(Input(Thrown("boom"), failures));

        Assert.True(text.Length <= CrashReportText.MaxLength);
        Assert.Contains("Source" + (RecentFailureLog.Capacity - 1), text, StringComparison.Ordinal);
        Assert.DoesNotContain("Source0:", text, StringComparison.Ordinal);
        Assert.Contains("at Ballast.Maui.Tests.CrashReportTextTests.Thrown", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    [InlineData(1000)]
    public void A_smaller_limit_is_kept_too(int limit)
    {
        var text = CrashReportText.Compose(Input(Thrown(new string('m', 5000)), [Failure(1, "A"), Failure(2, "B")]), limit);

        Assert.True(text.Length <= limit, $"{text.Length} characters against {limit}");
        Assert.StartsWith("Crash: ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cut_report_says_it_was_cut()
    {
        var text = CrashReportText.Compose(Input(Thrown(new string('m', 5000))), 120);

        Assert.EndsWith("[cut to fit]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Clipping_never_splits_a_surrogate_pair()
    {
        var emoji = string.Concat(Enumerable.Repeat("\U0001F68B", 10));

        var clipped = CrashReportText.Clip(emoji, 8);

        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F68B", 3)) + "…", clipped);
    }

    // ---- Nothing that places the person -------------------------------------------------------

    [Fact]
    public void A_request_address_keeps_its_host_and_loses_its_path_and_positions()
    {
        var scrubbed = CrashReportText.Scrub("The request to https://api.example.com/plan?fromPlace=45.812345,15.976543&toPlace=45.8,15.9 timed out.");

        Assert.Contains("[link: api.example.com]", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("45.8", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("15.9", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("fromPlace", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("/plan", scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("content://media/external/images/1234", "[uri]")]
    [InlineData("file:///data/user/0/app/files/x.pdf", "[uri]")]
    public void Any_other_address_becomes_a_marker(string input, string expected)
    {
        Assert.Equal(expected, CrashReportText.Scrub(input));
    }

    [Theory]
    [InlineData("rider@example.com could not be validated", "[email] could not be validated")]
    [InlineData("coordinates 45.81, 15.97 were rejected", "coordinates [coords] were rejected")]
    [InlineData("lat=45.81 lon=15.97", "lat=[position] lon=[position]")]
    [InlineData("Latitude: 45.812345", "Latitude=[position]")]
    [InlineData("value 15.976543 out of range", "value [number] out of range")]
    [InlineData("user 3f2a9c1e-5b7d-4e21-9a6f-0c8d1e2f3a4b not found", "user [id] not found")]
    public void Positions_emails_and_ids_are_replaced(string input, string expected)
    {
        Assert.Equal(expected, CrashReportText.Scrub(input));
    }

    [Theory]
    [InlineData("Could not find file '/data/user/0/example.app/files/tickets/ab12/x.bin'.")]
    [InlineData(@"Access to the path 'C:\Users\example\AppData\Local\Example\files\tickets' is denied.")]
    public void Absolute_file_paths_are_replaced(string input)
    {
        Assert.Contains("[path]", CrashReportText.Scrub(input), StringComparison.Ordinal);
    }

    [Fact]
    public void A_stack_frames_source_path_is_cut_to_the_file_name()
    {
        Assert.Equal(
            "at Example.Services.Foo.Bar() in Foo.cs:line 55",
            CrashReportText.Scrub("at Example.Services.Foo.Bar() in /_/Example/Services/Foo.cs:line 55"));
    }

    [Theory]
    [InlineData("Object reference not set to an instance of an object.")]
    [InlineData("Index was out of range. Must be non-negative and less than the size of the collection.")]
    [InlineData("App 1.4.2 on net10.0, build 57")]
    [InlineData("at Example.ViewModels.TripViewModel.<Load>d__12.MoveNext()")]
    public void Ordinary_text_is_left_alone(string input)
    {
        Assert.Equal(input, CrashReportText.Scrub(input));
    }

    [Fact]
    public void A_report_made_from_an_exception_that_quotes_a_position_holds_none_of_it()
    {
        var ex = Thrown("GET https://motis.example.org/api/v6/plan?fromPlace=45.8123456,15.9765432 returned 500 for rider@example.com");
        var recent = RecentFailure.Create(T0, "PlanClient", "plan to https://x.test/p?lat=45.81234 failed", "HTTP_500", "t1", null);

        var text = CrashReportText.Compose(Input(ex, [recent]));

        Assert.DoesNotContain("45.81", text, StringComparison.Ordinal);
        Assert.DoesNotContain("15.97", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fromPlace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rider@example.com", text, StringComparison.Ordinal);
        Assert.Contains("[link: motis.example.org]", text, StringComparison.Ordinal);
    }
}
