// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

using Ballast.AspNetCore;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Jobs.Tests;

/// <summary>
/// An admin action that queues background work is answered only by the copy that holds the job lock: the other copy says
/// 409 with a code and a wait, never a 5xx.
/// </summary>
public class RequireJobHolderAttributeTests
{
    private static (ResourceExecutingContext Context, DefaultHttpContext Http) ContextFor(IJobLeadership? leadership, RequireJobHolderOptions? words = null)
    {
        var services = new ServiceCollection();
        if (leadership is not null)
        {
            services.AddSingleton(leadership);
        }

        if (words is not null)
        {
            services.AddSingleton(words);
        }

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider(), TraceIdentifier = "trace-test" };
        http.Request.Path = "/feeds/7/fetch";
        http.Response.Body = new MemoryStream();
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        List<IFilterMetadata> filters = [];
        List<IValueProviderFactory> factories = [];
        return (new ResourceExecutingContext(action, filters, factories), http);
    }

    /// <summary>Runs the filter and says whether it let the request through to the action.</summary>
    private static async Task<bool> PassesAsync(ResourceExecutingContext context)
    {
        var passed = false;
        await new RequireJobHolderAttribute().OnResourceExecutionAsync(context, () =>
        {
            passed = true;
            return Task.FromResult(new ResourceExecutedContext(context, context.Filters));
        });
        return passed;
    }

    /// <summary>The JSON the filter wrote to the response.</summary>
    private static JsonElement BodyOf(DefaultHttpContext http)
    {
        var bytes = ((MemoryStream)http.Response.Body).ToArray();
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task A_copy_that_is_not_the_holder_answers_409_not_job_holder_with_a_wait()
    {
        var (context, http) = ContextFor(new StubLeadership(isHolder: false));

        var passed = await PassesAsync(context);

        Assert.False(passed);
        Assert.IsType<EmptyResult>(context.Result);
        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        Assert.Equal("application/problem+json; charset=utf-8", http.Response.ContentType);

        var problem = BodyOf(http);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", problem.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status409Conflict, problem.GetProperty("status").GetInt32());
        Assert.Equal("NOT_JOB_HOLDER", problem.GetProperty("code").GetString());
        Assert.Equal("This server copy is not running background jobs yet.", problem.GetProperty("title").GetString());
        Assert.Contains("Try again in a minute", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("/feeds/7/fetch", problem.GetProperty("instance").GetString());

        // The answer carries the trace reference in its body and in the header, as every problem of the library does.
        var traceId = problem.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrEmpty(traceId));
        Assert.Equal(traceId, http.Response.Headers[ProblemResponses.TraceIdHeader].ToString());
        Assert.Equal("30", http.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task The_title_and_detail_come_from_the_registered_words()
    {
        var words = new RequireJobHolderOptions { Title = "Busy here.", Detail = "Wait a moment." };
        var (context, http) = ContextFor(new StubLeadership(isHolder: false), words);

        Assert.False(await PassesAsync(context));

        var problem = BodyOf(http);
        Assert.Equal("Busy here.", problem.GetProperty("title").GetString());
        Assert.Equal("Wait a moment.", problem.GetProperty("detail").GetString());
        Assert.Equal("NOT_JOB_HOLDER", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_holder_is_let_through_and_nothing_is_added_to_the_response()
    {
        var (context, http) = ContextFor(new StubLeadership(isHolder: true));

        var passed = await PassesAsync(context);

        Assert.True(passed);
        Assert.Null(context.Result);
        Assert.False(http.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal(0, ((MemoryStream)http.Response.Body).Length);
    }

    [Fact]
    public async Task Where_no_job_lock_is_registered_the_action_is_answered_as_before()
    {
        var (context, _) = ContextFor(null);

        var passed = await PassesAsync(context);

        Assert.True(passed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task A_copy_that_becomes_the_holder_answers_the_next_call()
    {
        var leadership = new StubLeadership(isHolder: false);

        var (refused, _) = ContextFor(leadership);
        Assert.False(await PassesAsync(refused));

        leadership.IsHolder = true;
        var (allowed, _) = ContextFor(leadership);
        Assert.True(await PassesAsync(allowed));
    }

    [Fact]
    public void The_filter_is_a_resource_filter_so_it_runs_after_authorization()
    {
        // Resource filters run after authorization filters: a caller who is not signed in or not allowed
        // still gets 401 or 403 and never learns that a deploy is going on.
        Assert.IsAssignableFrom<IAsyncResourceFilter>(new RequireJobHolderAttribute());
        Assert.False(typeof(IAsyncAuthorizationFilter).IsAssignableFrom(typeof(RequireJobHolderAttribute)));
    }
}
