// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;

using Ballast.AspNetCore.Tests.Support;
using Ballast.Errors;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballast.AspNetCore.Tests.Handling;

/// <summary>
/// The exception handler, driven directly with an exception and a request, so each branch is reached without a server. Each test
/// reads the status, the headers, the body and the log entries the handler left.
/// </summary>
public class ProblemHandlingTests
{
    private const string Handler = "Ballast.AspNetCore.ProblemExceptionHandler";

    [Fact]
    public async Task An_app_exception_keeps_its_status_code_message_detail_and_extensions()
    {
        var error = new AppException("ITEM_LOCKED", "The item is locked.", 409, "Detail text.", new Dictionary<string, object?> { ["field"] = "X" });

        var answer = await Handle(error);

        Assert.True(answer.Handled);
        Assert.Equal(409, answer.Status);
        var json = answer.Json;
        Assert.Equal("The item is locked.", json.GetProperty("title").GetString());
        Assert.Equal("Detail text.", json.GetProperty("detail").GetString());
        Assert.Equal("ITEM_LOCKED", json.GetProperty("code").GetString());
        Assert.Equal("X", json.GetProperty("field").GetString());
        Assert.Equal(answer.Trace, json.GetProperty("traceId").GetString());
        Assert.Equal(answer.Trace, answer.Header("X-Trace-Id"));
    }

    [Fact]
    public async Task An_app_exception_without_a_detail_uses_its_message_as_the_detail()
    {
        var answer = await Handle(new AppException("ITEM_MISSING", "The item is missing.", 404));

        Assert.Equal("The item is missing.", answer.Json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_app_exception_from_a_5xx_is_logged_as_an_error_with_its_exception()
    {
        var error = new AppException("UPSTREAM_DOWN", "The upstream is down.", 502);

        var answer = await Handle(error);

        Assert.Equal(502, answer.Status);
        var entry = Assert.Single(answer.LogsAt(LogLevel.Error));
        Assert.Same(error, entry.Exception);
    }

    [Fact]
    public async Task An_app_exception_from_a_4xx_is_logged_as_information_without_its_exception()
    {
        var answer = await Handle(new AppException("ITEM_LOCKED", "The item is locked.", 409));

        var entry = Assert.Single(answer.Logs, record => record.Category == Handler);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task An_exception_no_mapping_matches_is_the_internal_error_with_no_text_of_the_failure()
    {
        var answer = await Handle(new InvalidOperationException("secret internal state"));

        Assert.Equal(500, answer.Status);
        Assert.Equal("INTERNAL_ERROR", answer.Json.GetProperty("code").GetString());
        Assert.Equal("Internal Server Error", answer.Json.GetProperty("title").GetString());
        Assert.Equal("Reference " + answer.Trace, answer.Json.GetProperty("detail").GetString());
        Assert.DoesNotContain("secret internal state", Encoding.UTF8.GetString(answer.Body), StringComparison.Ordinal);
        Assert.Equal(answer.Trace, answer.Json.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task An_unmatched_exception_is_logged_as_an_error_with_its_exception()
    {
        var error = new InvalidOperationException("boom");

        var answer = await Handle(error);

        var entry = Assert.Single(answer.LogsAt(LogLevel.Error));
        Assert.Same(error, entry.Exception);
        Assert.Contains(answer.Trace, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_matching_mapping_answers_the_request()
    {
        var options = new Action<ProblemResponseOptions>(options =>
        {
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "FIRST") { Title = "First." });
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SECOND") { Title = "Second." });
        });

        var answer = await Handle(new TimeoutException("slow"), options);

        Assert.Equal("FIRST", answer.Json.GetProperty("code").GetString());
        Assert.Equal("First.", answer.Json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_mapping_whose_predicate_does_not_match_is_skipped_for_the_next_one()
    {
        var options = new Action<ProblemResponseOptions>(options =>
        {
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is IOException, 503, "NOT_THIS"));
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "THIS_ONE"));
        });

        var answer = await Handle(new TimeoutException("slow"), options);

        Assert.Equal(503, answer.Status);
        Assert.Equal("THIS_ONE", answer.Json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_mapping_sets_the_status_the_code_the_title_the_detail_and_the_retry_after()
    {
        var options = new Action<ProblemResponseOptions>(options =>
        {
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SERVICE_BUSY")
            {
                Title = "The service is busy.",
                DetailFactory = (context, error) => "Reference " + ProblemResponses.TraceIdOf(context),
                RetryAfterSeconds = 30,
            });
        });

        var answer = await Handle(new TimeoutException("slow"), options);

        Assert.Equal(503, answer.Status);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.4", answer.Json.GetProperty("type").GetString());
        Assert.Equal("The service is busy.", answer.Json.GetProperty("title").GetString());
        Assert.Equal("Reference " + answer.Trace, answer.Json.GetProperty("detail").GetString());
        Assert.Equal("SERVICE_BUSY", answer.Json.GetProperty("code").GetString());
        Assert.Equal("/items/1", answer.Json.GetProperty("instance").GetString());
        Assert.Equal("30", answer.Header("Retry-After"));
    }

    [Fact]
    public async Task A_mapping_without_a_retry_after_writes_no_retry_after_header()
    {
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SERVICE_BUSY") { Title = "Busy." }));

        var answer = await Handle(new TimeoutException("slow"), options);

        Assert.False(answer.HasHeader("Retry-After"));
    }

    [Fact]
    public async Task A_mapping_without_a_detail_factory_writes_no_detail()
    {
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SERVICE_BUSY") { Title = "Busy." }));

        var answer = await Handle(new TimeoutException("slow"), options);

        Assert.False(answer.Json.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task A_title_factory_takes_precedence_over_a_fixed_title()
    {
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SERVICE_BUSY")
            {
                Title = "Fixed.",
                TitleFactory = (context, error) => "Computed for " + context.Request.Path.Value,
            }));

        var answer = await Handle(new TimeoutException("slow"), options);

        Assert.Equal("Computed for /items/1", answer.Json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_mapping_without_a_title_uses_the_registered_titles_for_its_status_and_code()
    {
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SERVICE_BUSY")));

        var answer = await Handle(new TimeoutException("slow"), options);

        Assert.Equal("Service Unavailable", answer.Json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_registered_titles_service_titles_the_problems_the_library_makes()
    {
        var answer = await Handle(new TimeoutException("slow"), options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SERVICE_BUSY")),
            services => services.AddSingleton<IProblemTitles>(new FixedTitles("Localised title.")));

        Assert.Equal("Localised title.", answer.Json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_app_exception_is_answered_before_any_mapping_is_checked()
    {
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(_ => true, 503, "MAPPED_ANYWAY")));

        var answer = await Handle(new AppException("ITEM_LOCKED", "The item is locked.", 409), options);

        Assert.Equal(409, answer.Status);
        Assert.Equal("ITEM_LOCKED", answer.Json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_mapping_at_500_or_above_is_logged_as_an_error_with_its_exception_unless_its_level_says_otherwise()
    {
        var error = new TimeoutException("slow");
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SERVICE_BUSY") { Title = "Busy." }));

        var answer = await Handle(error, options);

        var entry = Assert.Single(answer.Logs, record => record.Category == Handler);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(error, entry.Exception);
    }

    [Fact]
    public async Task A_mapping_that_sets_a_warning_level_is_logged_without_its_stack_trace()
    {
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 503, "SERVICE_BUSY")
            {
                Title = "Busy.",
                LogLevel = LogLevel.Warning,
            }));

        var answer = await Handle(new TimeoutException("slow"), options);

        var entry = Assert.Single(answer.Logs, record => record.Category == Handler);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains("TimeoutException", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mapping_below_500_is_logged_as_information_without_its_exception()
    {
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => e is TimeoutException, 409, "CONFLICT_SEEN") { Title = "Conflict." }));

        var answer = await Handle(new TimeoutException("slow"), options);

        Assert.Equal(409, answer.Status);
        var entry = Assert.Single(answer.Logs, record => record.Category == Handler);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task An_abandoned_request_is_answered_499_with_no_body_and_logged_at_debug_only()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var answer = await Handle(new InvalidOperationException("the connection was closed"), aborted: cancelled.Token);

        Assert.True(answer.Handled);
        Assert.Equal(499, answer.Status);
        Assert.Empty(answer.Body);
        Assert.False(answer.HasHeader("Content-Type"));
        Assert.Empty(answer.LogsAt(LogLevel.Error));
        Assert.Empty(answer.LogsAt(LogLevel.Warning));
        var entry = Assert.Single(answer.Logs, record => record.Category == Handler);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task The_trace_reference_is_written_to_the_header_of_an_abandoned_request_too()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var answer = await Handle(new InvalidOperationException("closed"), aborted: cancelled.Token);

        Assert.Equal(answer.Trace, answer.Header("X-Trace-Id"));
    }

    [Fact]
    public async Task A_mapping_is_checked_for_its_predicate_only_once_per_failure()
    {
        var calls = 0;
        var options = new Action<ProblemResponseOptions>(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(e => { calls++; return e is TimeoutException; }, 503, "SERVICE_BUSY") { Title = "Busy." }));

        await Handle(new TimeoutException("slow"), options);

        Assert.Equal(1, calls);
    }

    private static Task<Answer> Handle(Exception error, Action<ProblemResponseOptions>? options = null, CancellationToken aborted = default) =>
        Handle(error, options, null, aborted);

    private static async Task<Answer> Handle(Exception error, Action<ProblemResponseOptions>? options, Action<IServiceCollection>? extra, CancellationToken aborted = default)
    {
        var logs = new CapturedLogs();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(logs);
        });
        extra?.Invoke(services);
        services.AddProblemResponses(options);
        await using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IExceptionHandler>();
        var context = new DefaultHttpContext { RequestServices = provider, RequestAborted = aborted };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/items/1";
        var body = new MemoryStream();
        context.Response.Body = body;

        var handled = await handler.TryHandleAsync(context, error, CancellationToken.None);
        return new Answer(handled, context.Response, body.ToArray(), logs.Records);
    }

    private sealed record FixedTitles(string Text) : IProblemTitles
    {
        public string Title(int status, string code) => Text;
    }

    private sealed class Answer(bool handled, HttpResponse response, byte[] body, IReadOnlyList<CapturedLogs.LogRecord> logs)
    {
        public bool Handled { get; } = handled;

        public int Status { get; } = response.StatusCode;

        public byte[] Body { get; } = body;

        public IReadOnlyList<CapturedLogs.LogRecord> Logs { get; } = logs;

        public JsonElement Json => Body.Length == 0 ? default : JsonDocument.Parse(Body).RootElement.Clone();

        /// <summary>The trace reference the response carries, read from its header.</summary>
        public string Trace => Header("X-Trace-Id") ?? string.Empty;

        public string? Header(string name) =>
            response.Headers.TryGetValue(name, out var values) ? values.ToString() : null;

        public bool HasHeader(string name) => response.Headers.ContainsKey(name);

        public List<CapturedLogs.LogRecord> LogsAt(LogLevel level) =>
            Logs.Where(record => record.Level == level).ToList();
    }
}
