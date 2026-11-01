// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Json;

using Ballast.AspNetCore.Tests.Support;
using Ballast.Errors;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.AspNetCore.Tests.WireFormat;

/// <summary>
/// Pins the bytes of the answers the library writes. The expected JSON below is the JSON the framework's problem details have always
/// written: the same member names, the same order, the null members left out, the extension members at the top level, and the trace
/// reference from the request's activity. A client that reads these answers depends on them, so a change here must be deliberate.
/// </summary>
public class WireFormatTests
{
    private const string ProblemContentType = "application/problem+json; charset=utf-8";

    private static Task<Served> StartAsync() => Served.StartAsync(
        builder =>
        {
            builder.Services.AddControllers().AddApplicationPart(typeof(WireController).Assembly);
            builder.Services.AddProblemResponses();
        },
        app =>
        {
            app.UseProblemResponses();
            app.MapControllers();
            app.MapApiNotFound("/api");
            app.MapGet("/wire/hand", context => ProblemResponses.WriteValidationAsync(
                context,
                new Dictionary<string, string[]> { ["Name"] = ["The Name field is required."] },
                context.RequestAborted));
            app.MapGet("/wire/problem", context => ProblemResponses.WriteAsync(
                context,
                Problem.From(AppException.NotFound(ErrorCodes.NotFound, "Item not found.", "No item has id 42."), ProblemResponses.TraceIdOf(context), context.Request.Path.Value)));
        });

    [Fact]
    public async Task An_app_exception_is_answered_with_its_exact_problem_json()
    {
        await using var served = await StartAsync();

        using var response = await served.Client.GetAsync("/wire/refused");
        var body = await response.Content.ReadAsStringAsync();
        var trace = TraceIdHeader(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.10\",\"title\":\"The item is locked.\",\"status\":409," +
            "\"detail\":\"Detail text.\",\"instance\":\"/wire/refused\",\"code\":\"ITEM_LOCKED\",\"traceId\":\"" + trace + "\",\"field\":\"X\"}",
            body);
    }

    [Fact]
    public async Task A_validation_failure_is_answered_with_its_exact_problem_json()
    {
        await using var served = await StartAsync();

        using var response = await served.Client.PostAsJsonAsync("/wire/validate", new { });
        var body = await response.Content.ReadAsStringAsync();
        var trace = TraceIdHeader(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\",\"title\":\"One or more validation errors occurred.\",\"status\":400," +
            "\"errors\":{\"Name\":[\"The Name field is required.\"]},\"traceId\":\"" + trace + "\",\"code\":\"VALIDATION_FAILED\"}",
            body);
    }

    [Fact]
    public async Task An_unexpected_failure_is_a_500_that_carries_no_text_of_the_failure()
    {
        await using var served = await StartAsync();

        using var response = await served.Client.GetAsync("/wire/boom");
        var body = await response.Content.ReadAsStringAsync();
        var trace = TraceIdHeader(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.6.1\",\"title\":\"Internal Server Error\",\"status\":500," +
            "\"detail\":\"Reference " + trace + "\",\"instance\":\"/wire/boom\",\"code\":\"INTERNAL_ERROR\",\"traceId\":\"" + trace + "\"}",
            body);
        Assert.DoesNotContain("secret internal state", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_answer_keeps_its_body_and_carries_the_trace_header()
    {
        await using var served = await StartAsync();

        using var response = await served.Client.GetAsync("/wire/ok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"a\":1}", await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(TraceIdHeader(response)));
    }

    [Fact]
    public async Task The_hand_built_validation_answer_is_the_same_json_as_the_framework_one()
    {
        await using var served = await StartAsync();

        using var automatic = await served.Client.PostAsJsonAsync("/wire/validate", new { });
        using var handBuilt = await served.Client.GetAsync("/wire/hand");
        var automaticBody = await automatic.Content.ReadAsStringAsync();
        var handBody = await handBuilt.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, handBuilt.StatusCode);
        Assert.Equal(ProblemContentType, handBuilt.Content.Headers.ContentType?.ToString());
        Assert.Equal(Served.WithoutTraceId(automaticBody), Served.WithoutTraceId(handBody));
        Assert.Equal(TraceIdHeader(handBuilt), ReadTraceId(handBody));
    }

    [Fact]
    public async Task A_problem_written_by_hand_carries_its_own_members_and_the_trace_header()
    {
        await using var served = await StartAsync();

        using var response = await served.Client.GetAsync("/wire/problem");
        var body = await response.Content.ReadAsStringAsync();
        var trace = TraceIdHeader(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.5\",\"title\":\"Item not found.\",\"status\":404," +
            "\"detail\":\"No item has id 42.\",\"instance\":\"/wire/problem\",\"code\":\"NOT_FOUND\",\"traceId\":\"" + trace + "\"}",
            body);
    }

    [Fact]
    public async Task An_unknown_route_under_the_prefix_is_answered_with_its_exact_problem_json()
    {
        await using var served = await StartAsync();

        using var response = await served.Client.GetAsync("/api/nope");
        var body = await response.Content.ReadAsStringAsync();
        var trace = TraceIdHeader(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.5\",\"title\":\"Not Found\",\"status\":404," +
            "\"detail\":\"No endpoint is at this address.\",\"instance\":\"/api/nope\",\"code\":\"ENDPOINT_NOT_FOUND\",\"traceId\":\"" + trace + "\"}",
            body);
    }

    [Fact]
    public async Task The_failure_is_logged_once_inside_a_scope_that_carries_the_trace_reference()
    {
        await using var served = await StartAsync();

        using var response = await served.Client.GetAsync("/wire/refused");
        var trace = TraceIdHeader(response);

        var entries = served.Logs.Records.Where(record => record.Message.Contains("ITEM_LOCKED", StringComparison.Ordinal)).ToList();
        var entry = Assert.Single(entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains(entry.Scopes, scope => scope is IDictionary<string, object> values
            && values.TryGetValue("TraceId", out var value) && string.Equals(value?.ToString(), trace, StringComparison.Ordinal));
    }

    private static string TraceIdHeader(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("X-Trace-Id", out var values), "The response has no X-Trace-Id header.");
        return Assert.Single(values);
    }

    private static string ReadTraceId(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("traceId").GetString() ?? string.Empty;
    }
}
