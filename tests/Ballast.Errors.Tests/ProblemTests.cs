// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

namespace Ballast.Errors.Tests;

public class ProblemTests
{
    [Fact]
    public void From_carries_the_fields_an_answer_needs()
    {
        var error = new AppException(ErrorCodes.NotFound, "Station not found.", 404, "No station has id 42.");

        var problem = Problem.From(error, traceId: "test-trace-123", instance: "/stations/42");

        Assert.Equal(404, problem.Status);
        Assert.Equal("Station not found.", problem.Title);
        Assert.Equal("No station has id 42.", problem.Detail);
        Assert.Equal("NOT_FOUND", problem.Code);
        Assert.Equal("test-trace-123", problem.TraceId);
        Assert.Equal("/stations/42", problem.Instance);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", problem.Type);
    }

    [Fact]
    public void From_takes_the_title_from_the_message_and_the_detail_from_the_exception_detail()
    {
        var error = new AppException(ErrorCodes.ValidationFailed, "The date is in the past.", 422, "Departure 2020-01-01 has passed.");

        var problem = Problem.From(error);

        Assert.Equal("The date is in the past.", problem.Title);
        Assert.Equal("Departure 2020-01-01 has passed.", problem.Detail);
    }

    [Fact]
    public void From_uses_the_message_as_the_detail_when_the_exception_has_no_detail()
    {
        var error = new AppException(ErrorCodes.NotFound, "Station not found.", 404);

        var problem = Problem.From(error);

        Assert.Equal("Station not found.", problem.Title);
        Assert.Equal("Station not found.", problem.Detail);
    }

    [Fact]
    public void From_leaves_the_trace_and_the_instance_null_when_none_are_given()
    {
        var problem = Problem.From(AppException.NotFound(ErrorCodes.NotFound, "Station not found."));

        Assert.Null(problem.TraceId);
        Assert.Null(problem.Instance);
    }

    [Fact]
    public void From_copies_the_extensions_of_the_exception()
    {
        var error = AppException.Unprocessable(ErrorCodes.ValidationFailed, "Invalid.", extensions: new Dictionary<string, object?> { ["field"] = "date" });

        var problem = Problem.From(error);

        Assert.Equal("date", problem.Extensions["field"]);
    }

    [Fact]
    public void From_takes_the_type_from_the_status_of_the_exception()
    {
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.21", Problem.From(AppException.Unprocessable(ErrorCodes.ValidationFailed, "Invalid.")).Type);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.4", Problem.From(AppException.Forbidden(ErrorCodes.NotFound, "No.")).Type);
    }

    [Fact]
    public void From_refuses_a_null_exception()
    {
        Assert.Throws<ArgumentNullException>(() => Problem.From(null!));
    }

    [Fact]
    public void InternalError_is_500_with_the_fixed_title_code_and_a_reference_only()
    {
        var problem = Problem.InternalError("trace-abc", "/stations");

        Assert.Equal(500, problem.Status);
        Assert.Equal("Internal Server Error", problem.Title);
        Assert.Equal("Reference trace-abc", problem.Detail);
        Assert.Equal("INTERNAL_ERROR", problem.Code);
        Assert.Equal("trace-abc", problem.TraceId);
        Assert.Equal("/stations", problem.Instance);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.1", problem.Type);
        Assert.Empty(problem.Extensions);
    }

    [Fact]
    public void InternalError_never_carries_the_text_of_the_failure()
    {
        // The failure stays in the server's log. InternalError takes no exception, so no part of its text can reach the answer.
        var failure = new InvalidOperationException("SQL error 4060: cannot open database 'ExampleDb' at Example.Store.Open()");

        var problem = Problem.InternalError("trace-1");

        Assert.NotNull(failure);
        Assert.DoesNotContain("4060", problem.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("4060", problem.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("ExampleDb", problem.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("Example.Store", problem.Detail!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void InternalError_refuses_a_blank_trace_reference(string? traceId)
    {
        Assert.ThrowsAny<ArgumentException>(() => Problem.InternalError(traceId!));
    }
}
