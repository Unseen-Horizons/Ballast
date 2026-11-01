// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.AspNetCore.Tests.Support;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Ballast.AspNetCore.Tests.Endpoints;

/// <summary>The fallback that answers the unknown routes under an API prefix.</summary>
public class MapApiNotFoundTests
{
    [Fact]
    public async Task An_unknown_route_under_the_prefix_is_answered_404_with_the_code()
    {
        await using var served = await Start(app => app.MapApiNotFound("/api"));

        using var response = await served.Client.GetAsync("/api/missing/thing");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("\"code\":\"ENDPOINT_NOT_FOUND\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_post_to_an_unknown_route_under_the_prefix_is_answered_the_same_way()
    {
        await using var served = await Start(app => app.MapApiNotFound("/api"));

        using var response = await served.Client.PostAsync("/api/missing", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("ENDPOINT_NOT_FOUND", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_prefix_root_itself_is_answered_as_an_unknown_route()
    {
        await using var served = await Start(app => app.MapApiNotFound("/api"));

        using var response = await served.Client.GetAsync("/api");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("ENDPOINT_NOT_FOUND", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_path_that_only_shares_the_prefix_text_is_not_touched()
    {
        await using var served = await Start(app => app.MapApiNotFound("/api"));

        using var response = await served.Client.GetAsync("/apiary/x");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("ENDPOINT_NOT_FOUND", body, StringComparison.Ordinal);
        Assert.Empty(body);
    }

    [Fact]
    public async Task A_path_outside_the_prefix_keeps_the_application_fallback()
    {
        await using var served = await Start(app =>
        {
            app.MapApiNotFound("/api");
            app.MapFallback(() => Results.Text("the site", "text/plain"));
        });

        using var response = await served.Client.GetAsync("/some/page");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("the site", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_known_route_under_the_prefix_is_not_affected()
    {
        await using var served = await Start(app =>
        {
            app.MapApiNotFound("/api");
            app.MapGet("/api/items/{id}", (string id) => Results.Ok(new { id }));
        });

        using var response = await served.Client.GetAsync("/api/items/7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"id\":\"7\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_known_route_called_with_an_unsupported_method_is_answered_as_unknown()
    {
        await using var served = await Start(app =>
        {
            app.MapApiNotFound("/api");
            app.MapGet("/api/items/{id}", (string id) => Results.Ok(new { id }));
        });

        using var response = await served.Client.PostAsync("/api/items/7", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("ENDPOINT_NOT_FOUND", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_prefix_of_several_segments_is_matched_by_segment()
    {
        await using var served = await Start(app => app.MapApiNotFound("/api/v1/"));

        using var inside = await served.Client.GetAsync("/api/v1/unknown");
        using var outside = await served.Client.GetAsync("/api/v2/unknown");

        Assert.Contains("ENDPOINT_NOT_FOUND", await inside.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(await outside.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_detail_of_the_answer_comes_from_the_options()
    {
        await using var served = await Start(
            app => app.MapApiNotFound("/api"),
            options => options.EndpointNotFoundDetail = context => "Localised for " + context.Request.Path.Value);

        using var response = await served.Client.GetAsync("/api/gone");

        Assert.Contains("\"detail\":\"Localised for /api/gone\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("/api/{id}")]
    [InlineData("/api/*")]
    [InlineData("/api/a b")]
    public void An_invalid_prefix_is_refused(string prefix)
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        Assert.ThrowsAny<ArgumentException>(() => app.MapApiNotFound(prefix));
    }

    [Fact]
    public void A_null_prefix_is_refused()
    {
        var app = WebApplication.CreateBuilder().Build();

        Assert.ThrowsAny<ArgumentException>(() => app.MapApiNotFound(null!));
    }

    private static Task<Served> Start(Action<WebApplication> pipeline, Action<ProblemResponseOptions>? options = null) => Served.StartAsync(
        builder => builder.Services.AddProblemResponses(options),
        app =>
        {
            app.UseProblemResponses();
            pipeline(app);
        });
}
