// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;
using System.Text.Json;

using Ballast.Errors;

namespace Ballast.Errors.Tests;

public class ProblemReaderTests
{
    [Fact]
    public async Task Reads_the_code_title_detail_and_trace_id_of_a_problem()
    {
        using var response = Json("""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.21","title":"Invalid.","status":422,"detail":"Bad request detail","code":"VALIDATION_FAILED","traceId":"trace-xyz"}""", HttpStatusCode.UnprocessableEntity);

        var info = await ProblemReader.TryReadAsync(response);

        Assert.NotNull(info);
        Assert.Equal("VALIDATION_FAILED", info.Code);
        Assert.Equal("Invalid.", info.Title);
        Assert.Equal("Bad request detail", info.Detail);
        Assert.Equal("trace-xyz", info.TraceId);
    }

    [Fact]
    public async Task Reads_back_the_members_of_a_problem_built_by_the_library()
    {
        var problem = Problem.From(AppException.NotFound(ErrorCodes.NotFound, "Station not found.", "No station has id 42."), "trace-9", "/stations/42");
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = problem.Type,
            ["title"] = problem.Title,
            ["status"] = problem.Status,
            ["detail"] = problem.Detail,
            ["instance"] = problem.Instance,
            ["code"] = problem.Code,
            ["traceId"] = problem.TraceId,
        });
        using var response = Json(body, HttpStatusCode.NotFound);

        var info = await ProblemReader.TryReadAsync(response);

        Assert.Equal(new ProblemInfo("NOT_FOUND", "Station not found.", "No station has id 42.", "trace-9"), info);
    }

    [Fact]
    public async Task Reads_the_message_member_as_the_title_when_there_is_no_title()
    {
        using var response = Json("""{"message":"Old text.","code":"OLD_CODE"}""");

        var info = await ProblemReader.TryReadAsync(response);

        Assert.Equal(new ProblemInfo("OLD_CODE", "Old text.", null, null), info);
    }

    [Fact]
    public async Task Reads_the_error_member_as_the_title_when_there_is_neither_a_title_nor_a_message()
    {
        using var response = Json("""{"error":"Old error."}""");

        var info = await ProblemReader.TryReadAsync(response);

        Assert.Equal(new ProblemInfo(null, "Old error.", null, null), info);
    }

    [Fact]
    public async Task The_title_wins_over_the_message_and_the_error()
    {
        using var response = Json("""{"title":"New title.","message":"Old message.","error":"Old error."}""");

        var info = await ProblemReader.TryReadAsync(response);

        Assert.Equal("New title.", info?.Title);
    }

    [Fact]
    public async Task A_title_that_is_not_a_string_falls_back_to_the_message()
    {
        using var response = Json("""{"title":5,"message":"Old message."}""");

        var info = await ProblemReader.TryReadAsync(response);

        Assert.Equal("Old message.", info?.Title);
    }

    [Fact]
    public async Task A_code_that_is_not_a_string_is_ignored_and_never_read_from_the_title()
    {
        using var response = Json("""{"code":5,"title":"Invalid."}""");

        var info = await ProblemReader.TryReadAsync(response);

        Assert.Equal(new ProblemInfo(null, "Invalid.", null, null), info);
    }

    [Fact]
    public async Task A_bare_JSON_string_is_the_title_with_no_code()
    {
        using var response = Json("\"Something went wrong.\"");

        var info = await ProblemReader.TryReadAsync(response);

        Assert.Equal(new ProblemInfo(null, "Something went wrong.", null, null), info);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>Bad gateway</body></html>")]
    [InlineData("[\"a\"]")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{\"foo\":1}")]
    [InlineData("{\"code\":5,\"title\":false}")]
    [InlineData("\"   \"")]
    public async Task A_body_that_carries_no_problem_fields_gives_null(string body)
    {
        using var response = Json(body);

        Assert.Null(await ProblemReader.TryReadAsync(response));
    }

    [Fact]
    public async Task A_null_response_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ProblemReader.TryReadAsync(null!));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.BadRequest) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };
}
