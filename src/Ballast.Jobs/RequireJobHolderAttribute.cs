// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Ballast.AspNetCore;
using Ballast.Errors;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Jobs;

/// <summary>
/// Marks an action that starts background work (an import, an export, a price-source run): it is answered only by the copy
/// that holds the job lock, because the work it queues would otherwise run in a copy whose workers are not running, or beside
/// the holder's own.
/// <para>
/// It runs after authorization, so a caller who is not signed in or not allowed still gets 401 or 403 first. On a copy that is
/// not the holder it answers 409 <see cref="ErrorCode"/> with <c>Retry-After</c> (<see cref="RetryAfterSeconds"/>). 409 is the
/// answer for "busy, try again", and it keeps a deploy out of the 5xx alerts. The title and the detail come from
/// <see cref="RequireJobHolderOptions"/>, and the answer is written by <see cref="ProblemResponses"/>, so it carries the
/// trace reference like every other problem. Where no <see cref="IJobLeadership"/> is registered the action is answered as before.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireJobHolderAttribute : Attribute, IAsyncResourceFilter
{
    /// <summary>The stable code of the 409 answer: <c>NOT_JOB_HOLDER</c>. It is a public contract and is never changed.</summary>
    public const string ErrorCode = "NOT_JOB_HOLDER";

    /// <summary>The wait, in seconds, that the <c>Retry-After</c> header of the 409 answer gives: 30.</summary>
    public const int RetryAfterSeconds = 30;

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var leadership = http.RequestServices.GetService<IJobLeadership>();
        if (leadership is null || leadership.IsHolder)
        {
            await next();
            return;
        }

        var words = http.RequestServices.GetService<RequireJobHolderOptions>() ?? new RequireJobHolderOptions();
        http.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        var problem = new Problem
        {
            Type = ProblemTypes.For(StatusCodes.Status409Conflict),
            Title = words.Title,
            Status = StatusCodes.Status409Conflict,
            Detail = words.Detail,
            Instance = http.Request.Path.HasValue ? http.Request.Path.Value : null,
            Code = ErrorCode,
            TraceId = ProblemResponses.TraceIdOf(http),
        };

        // The problem is written here, so the action does not run. An empty result leaves the response as it was written.
        await ProblemResponses.WriteAsync(http, problem, http.RequestAborted);
        context.Result = new EmptyResult();
    }
}
