// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

namespace Ballast.Errors.Tests;

public class UpstreamExceptionTests
{
    [Fact]
    public void Is_a_502_app_exception_that_keeps_the_host_status_and_address()
    {
        var error = new UpstreamException("api.example.test", 503, "https://api.example.test/v1/stations", ErrorCodes.UpstreamUnavailable, "The feed did not answer.");

        Assert.IsAssignableFrom<AppException>(error);
        Assert.Equal(502, error.StatusCode);
        Assert.Equal("UPSTREAM_UNAVAILABLE", error.Code);
        Assert.Equal("The feed did not answer.", error.Message);
        Assert.Null(error.Detail);
        Assert.Equal("api.example.test", error.Host);
        Assert.Equal(503, error.UpstreamStatusCode);
        Assert.Equal("https://api.example.test/v1/stations", error.RequestUri);
    }

    [Fact]
    public void The_upstream_status_is_stored_as_given()
    {
        var error = new UpstreamException("api.example.test", 0, "https://api.example.test/", ErrorCodes.UpstreamUnavailable, "Unavailable.");

        Assert.Equal(0, error.UpstreamStatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_host_is_refused(string? host)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => new UpstreamException(host!, 500, "https://api.example.test/", ErrorCodes.UpstreamUnavailable, "Unavailable."));

        Assert.Equal("host", ((ArgumentException)error).ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_request_address_is_refused(string? requestUri)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => new UpstreamException("api.example.test", 500, requestUri!, ErrorCodes.UpstreamUnavailable, "Unavailable."));

        Assert.Equal("requestUri", ((ArgumentException)error).ParamName);
    }

    [Fact]
    public void A_blank_code_is_refused_as_for_any_app_exception()
    {
        Assert.ThrowsAny<ArgumentException>(() => new UpstreamException("api.example.test", 500, "https://api.example.test/", "", "Unavailable."));
    }

    [Fact]
    public void Problem_from_it_does_not_write_the_host_status_or_address()
    {
        var error = new UpstreamException("upstream.example.test", 500, "https://upstream.example.test/feed?key=secret", ErrorCodes.UpstreamUnavailable, "Unavailable.", "The upstream did not answer.");

        var problem = Problem.From(error, "trace-1");

        Assert.Equal(502, problem.Status);
        Assert.Equal("The upstream did not answer.", problem.Detail);
        Assert.Empty(problem.Extensions);
        Assert.DoesNotContain("upstream.example.test", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", problem.Detail, StringComparison.Ordinal);
    }
}
