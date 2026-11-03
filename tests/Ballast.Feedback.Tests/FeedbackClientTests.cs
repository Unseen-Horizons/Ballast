// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Ballast.Feedback.Tests;

/// <summary>
/// The app's send: the five outcomes the app shows words for, the JSON it posts, and the trimming it does before it posts.
/// </summary>
public class FeedbackClientTests
{
    private static HttpClient Http(FakeHttpHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.example.test/") };

    private static FeedbackRequest Valid(string message = "The map is empty.") =>
        new() { Message = message, AppVersion = "0.5.0", Platform = "Android" };

    private static HttpResponseMessage NoContent() => Status(HttpStatusCode.NoContent);

    /// <summary>A response with no body, carrying an empty content the way a real answer does.</summary>
    private static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent(string.Empty) };

    // ── Empty ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_message_is_Empty_and_nothing_is_posted(string message)
    {
        var handler = new FakeHttpHandler();

        var result = await new FeedbackClient(Http(handler)).SendAsync(new FeedbackRequest { Message = message });

        Assert.Equal(FeedbackSendOutcome.Empty, result.Outcome);
        Assert.Equal("FeedbackEmpty", result.Code);
        Assert.Empty(handler.Requests);
    }

    // ── Sent and the wire ────────────────────────────────────────────────────

    [Fact]
    public async Task A_204_is_Sent_and_the_message_is_posted_to_the_route()
    {
        var handler = new FakeHttpHandler(NoContent());

        var result = await new FeedbackClient(Http(handler)).SendAsync(Valid());

        Assert.Equal(FeedbackSendOutcome.Sent, result.Outcome);
        Assert.Equal(string.Empty, result.Code);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.example.test/feedback", sent.Url.AbsoluteUri);
    }

    [Fact]
    public async Task The_body_uses_the_wire_names_the_server_reads()
    {
        var handler = new FakeHttpHandler(NoContent());
        var request = new FeedbackRequest
        {
            Message = "Hi",
            Subject = "Subject",
            Email = "rider@example.test",
            Category = "Bug",
            ContextId = 42,
            ContextName = "Dugo Selo",
            AppVersion = "0.5.0",
            Platform = "Android",
        };

        await new FeedbackClient(Http(handler)).SendAsync(request);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        var root = body.RootElement;
        Assert.Equal("Hi", root.GetProperty("message").GetString());
        Assert.Equal("Subject", root.GetProperty("subject").GetString());
        Assert.Equal("rider@example.test", root.GetProperty("email").GetString());
        Assert.Equal("Bug", root.GetProperty("category").GetString());
        Assert.Equal(42, root.GetProperty("stationId").GetInt32());
        Assert.Equal("Dugo Selo", root.GetProperty("stationName").GetString());
        Assert.Equal("0.5.0", root.GetProperty("appVersion").GetString());
        Assert.Equal("Android", root.GetProperty("platform").GetString());
    }

    [Fact]
    public async Task Text_is_trimmed_and_blank_optional_text_is_sent_as_null()
    {
        var handler = new FakeHttpHandler(NoContent());
        var request = new FeedbackRequest
        {
            Message = "  Hi there  ",
            Subject = "   ",
            Email = " rider@example.test ",
            Category = "  Other ",
            ContextName = "  ",
            AppVersion = " 0.5.0 ",
        };

        await new FeedbackClient(Http(handler)).SendAsync(request);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        var root = body.RootElement;
        Assert.Equal("Hi there", root.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subject").ValueKind);
        Assert.Equal("rider@example.test", root.GetProperty("email").GetString());
        Assert.Equal("Other", root.GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("stationName").ValueKind);
        // The app version is passed through as the app gave it.
        Assert.Equal(" 0.5.0 ", root.GetProperty("appVersion").GetString());
    }

    [Fact]
    public async Task The_route_is_an_option()
    {
        var handler = new FakeHttpHandler(NoContent());

        await new FeedbackClient(Http(handler), options: new FeedbackOptions { Route = "api/v2/feedback" }).SendAsync(Valid());

        Assert.Equal("https://api.example.test/api/v2/feedback", handler.Requests.Single().Url.AbsoluteUri);
    }

    [Fact]
    public void A_blank_route_is_refused()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new FeedbackClient(http, options: new FeedbackOptions { Route = " " }));
    }

    [Fact]
    public async Task A_null_request_is_refused() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => new FeedbackClient(Http(new FakeHttpHandler())).SendAsync(null!));

    // ── NotConfigured (501) ──────────────────────────────────────────────────

    [Fact]
    public async Task A_501_is_NotConfigured_with_the_server_s_problem_fields()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.NotImplemented,
            """{"code":"FEEDBACK_NOT_CONFIGURED","title":"Feedback is not set up.","detail":"No mail key.","traceId":"trace-501"}"""));

        var result = await new FeedbackClient(Http(handler), new CapturingLogger<FeedbackClient>()).SendAsync(Valid());

        Assert.Equal(FeedbackSendOutcome.NotConfigured, result.Outcome);
        Assert.Equal("FEEDBACK_NOT_CONFIGURED", result.Code);
        Assert.Equal("Feedback is not set up.", result.ServerTitle);
        Assert.Equal("No mail key.", result.ServerDetail);
        Assert.Equal("trace-501", result.TraceId);
    }

    [Fact]
    public async Task A_501_with_no_body_uses_the_fallback_code_and_no_title()
    {
        var handler = new FakeHttpHandler(Status(HttpStatusCode.NotImplemented));

        var result = await new FeedbackClient(Http(handler)).SendAsync(Valid());

        Assert.Equal(FeedbackSendOutcome.NotConfigured, result.Outcome);
        Assert.Equal("FeedbackNotConfigured", result.Code);
        Assert.Null(result.ServerTitle);
    }

    [Fact]
    public async Task A_501_is_logged_as_a_warning()
    {
        var logger = new CapturingLogger<FeedbackClient>();
        var handler = new FakeHttpHandler(Status(HttpStatusCode.NotImplemented));

        await new FeedbackClient(Http(handler), logger).SendAsync(Valid());

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("501", StringComparison.Ordinal));
    }

    // ── NotAvailable (404) ───────────────────────────────────────────────────

    [Fact]
    public async Task A_404_is_NotAvailable()
    {
        var handler = new FakeHttpHandler(Status(HttpStatusCode.NotFound));

        var result = await new FeedbackClient(Http(handler)).SendAsync(Valid());

        Assert.Equal(FeedbackSendOutcome.NotAvailable, result.Outcome);
        Assert.Equal("FeedbackNotAvailable", result.Code);
    }

    [Fact]
    public async Task A_404_with_the_server_s_code_keeps_that_code()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.NotFound, """{"code":"NOT_FOUND","title":"Not here."}"""));

        var result = await new FeedbackClient(Http(handler)).SendAsync(Valid());

        Assert.Equal("NOT_FOUND", result.Code);
        Assert.Equal("Not here.", result.ServerTitle);
    }

    // ── Rejected (any other error) ───────────────────────────────────────────

    [Fact]
    public async Task A_400_is_Rejected_with_the_server_s_code_and_trace()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.BadRequest,
            """{"code":"FEEDBACK_INVALID","title":"Feedback is not valid.","detail":"message is required.","traceId":"trace-400"}"""));

        var result = await new FeedbackClient(Http(handler)).SendAsync(Valid());

        Assert.Equal(FeedbackSendOutcome.Rejected, result.Outcome);
        Assert.Equal("FEEDBACK_INVALID", result.Code);
        Assert.Equal("message is required.", result.ServerDetail);
        Assert.Equal("trace-400", result.TraceId);
    }

    [Fact]
    public async Task A_500_with_no_problem_fields_is_Rejected_with_the_fallback_code()
    {
        var handler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("Something broke.") });

        var result = await new FeedbackClient(Http(handler), new CapturingLogger<FeedbackClient>()).SendAsync(Valid());

        Assert.Equal(FeedbackSendOutcome.Rejected, result.Outcome);
        Assert.Equal("FeedbackRejected", result.Code);
        Assert.Null(result.ServerTitle);
    }

    // ── Unreachable ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_connection_failure_is_Unreachable_and_the_text_is_not_lost_to_the_client()
    {
        var handler = new FakeHttpHandler { ThrowOnSend = new HttpRequestException("No network.") };
        var request = Valid();

        var result = await new FeedbackClient(Http(handler), new CapturingLogger<FeedbackClient>()).SendAsync(request);

        Assert.Equal(FeedbackSendOutcome.Unreachable, result.Outcome);
        Assert.Equal("FeedbackUnreachable", result.Code);
        // The client never edits the caller's request: the app keeps the text it typed.
        Assert.Equal("The map is empty.", request.Message);
    }

    [Fact]
    public async Task A_timeout_is_Unreachable()
    {
        var handler = new FakeHttpHandler { ThrowOnSend = new TaskCanceledException("Timed out.") };

        var result = await new FeedbackClient(Http(handler)).SendAsync(Valid());

        Assert.Equal(FeedbackSendOutcome.Unreachable, result.Outcome);
    }

    [Fact]
    public async Task Every_outcome_has_a_code_the_app_can_log()
    {
        var outcomes = new[]
        {
            await new FeedbackClient(Http(new FakeHttpHandler(NoContent()))).SendAsync(Valid()),
            await new FeedbackClient(Http(new FakeHttpHandler(Status(HttpStatusCode.NotImplemented)))).SendAsync(Valid()),
            await new FeedbackClient(Http(new FakeHttpHandler(Status(HttpStatusCode.NotFound)))).SendAsync(Valid()),
            await new FeedbackClient(Http(new FakeHttpHandler(Status(HttpStatusCode.BadRequest)))).SendAsync(Valid()),
            await new FeedbackClient(Http(new FakeHttpHandler { ThrowOnSend = new HttpRequestException() })).SendAsync(Valid()),
            await new FeedbackClient(Http(new FakeHttpHandler())).SendAsync(new FeedbackRequest { Message = " " }),
        };

        Assert.Equal(
            new[]
            {
                FeedbackSendOutcome.Sent,
                FeedbackSendOutcome.NotConfigured,
                FeedbackSendOutcome.NotAvailable,
                FeedbackSendOutcome.Rejected,
                FeedbackSendOutcome.Unreachable,
                FeedbackSendOutcome.Empty,
            },
            outcomes.Select(o => o.Outcome).ToArray());
    }
}
