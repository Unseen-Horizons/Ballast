// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

using Ballast.Errors;

namespace Ballast.Errors.Tests;

public class AppExceptionTests
{
    [Fact]
    public void Carries_the_code_status_message_and_detail()
    {
        var error = new AppException(ErrorCodes.NotFound, "Station not found.", 404, "No station has id 42.");

        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("Station not found.", error.Message);
        Assert.Equal("No station has id 42.", error.Detail);
        Assert.Empty(error.Extensions);
    }

    [Fact]
    public void Detail_is_null_when_none_is_given()
    {
        var error = new AppException(ErrorCodes.NotFound, "Station not found.", 404);

        Assert.Null(error.Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_code_is_refused(string? code)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => new AppException(code!, "Message.", 400));

        Assert.Equal("code", ((ArgumentException)error).ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_message_is_refused(string? message) =>
        Assert.ThrowsAny<ArgumentException>(() => new AppException(ErrorCodes.NotFound, message!, 404));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_detail_is_refused_and_is_not_read_as_no_detail(string detail) =>
        Assert.ThrowsAny<ArgumentException>(() => new AppException(ErrorCodes.NotFound, "Message.", 404, detail: detail!));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(200)]
    [InlineData(399)]
    [InlineData(600)]
    [InlineData(int.MaxValue)]
    public void A_status_outside_400_to_599_is_refused(int status)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new AppException(ErrorCodes.NotFound, "Message.", status));

        Assert.Equal("status", error.ParamName);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(418)]
    [InlineData(499)]
    [InlineData(599)]
    public void A_status_from_400_to_599_is_accepted(int status) =>
        Assert.Equal(status, new AppException(ErrorCodes.NotFound, "Message.", status).StatusCode);

    [Fact]
    public void The_status_has_no_default_while_the_detail_and_the_extensions_are_optional()
    {
        var constructor = typeof(AppException).GetConstructor([typeof(string), typeof(string), typeof(int), typeof(string), typeof(IReadOnlyDictionary<string, object?>)]);

        Assert.NotNull(constructor);
        var parameters = constructor.GetParameters();
        Assert.False(parameters[0].HasDefaultValue, "code");
        Assert.False(parameters[1].HasDefaultValue, "message");
        Assert.False(parameters[2].HasDefaultValue, "status");
        Assert.True(parameters[3].HasDefaultValue, "detail");
        Assert.True(parameters[4].HasDefaultValue, "extensions");
    }

    [Fact]
    public void Each_factory_gives_its_status_and_keeps_the_code_message_and_detail()
    {
        var errors = new[]
        {
            AppException.BadRequest("CODE", "Message.", "Detail."),
            AppException.Forbidden("CODE", "Message.", "Detail."),
            AppException.NotFound("CODE", "Message.", "Detail."),
            AppException.Conflict("CODE", "Message.", "Detail."),
            AppException.Unprocessable("CODE", "Message.", "Detail."),
            AppException.Upstream("CODE", "Message.", "Detail."),
        };

        Assert.Equal([400, 403, 404, 409, 422, 502], errors.Select(e => e.StatusCode));
        Assert.All(errors, e =>
        {
            Assert.Equal("CODE", e.Code);
            Assert.Equal("Message.", e.Message);
            Assert.Equal("Detail.", e.Detail);
        });
    }

    [Fact]
    public void A_factory_without_a_detail_leaves_the_detail_null()
    {
        Assert.Null(AppException.NotFound(ErrorCodes.NotFound, "Station not found.").Detail);
    }

    [Fact]
    public void Extensions_are_copied_so_a_later_change_to_the_source_does_not_show()
    {
        var source = new Dictionary<string, object?> { ["field"] = "date" };
        var error = AppException.Unprocessable(ErrorCodes.ValidationFailed, "Invalid.", extensions: source);

        source["field"] = "other";
        source["added"] = 1;

        Assert.Equal("date", error.Extensions["field"]);
        Assert.False(error.Extensions.ContainsKey("added"));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("title")]
    [InlineData("status")]
    [InlineData("detail")]
    [InlineData("instance")]
    [InlineData("code")]
    [InlineData("traceId")]
    [InlineData("TRACEID")]
    [InlineData("Code")]
    public void An_extension_named_like_a_problem_member_is_refused(string name)
    {
        var extensions = new Dictionary<string, object?> { [name] = "value" };

        var error = Assert.Throws<ArgumentException>(() => AppException.BadRequest(ErrorCodes.ValidationFailed, "Invalid.", extensions: extensions));

        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_extension_name_is_refused()
    {
        var extensions = new Dictionary<string, object?> { [" "] = "value" };

        Assert.Throws<ArgumentException>(() => AppException.BadRequest(ErrorCodes.ValidationFailed, "Invalid.", extensions: extensions));
    }

    [Fact]
    public void Its_message_is_its_message_and_it_is_an_exception()
    {
        Exception error = AppException.NotFound(ErrorCodes.NotFound, "Station not found.");

        Assert.Equal("Station not found.", error.Message);
    }
}
