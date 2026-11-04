// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.AspNetCore;

/// <summary>Answers the unknown routes of an API prefix with a problem.</summary>
public static class ProblemEndpointExtensions
{
    /// <summary>
    /// Answers a request for a route under <paramref name="prefix"/> that no endpoint handles with 404 and the code
    /// <see cref="ErrorCodes.EndpointNotFound"/>, as a problem. Requests outside the prefix are not touched: they keep the framework's
    /// own answer. The route is a fallback, so it takes only what no other endpoint matches. A request to a known route with a method
    /// that route does not support is also answered this way, because the framework's 405 is not reached.
    /// </summary>
    /// <param name="endpoints">The endpoint builder, usually the application.</param>
    /// <param name="prefix">The first segment or segments of the API's routes, such as <c>/api</c>. It must start with '/', must not be
    /// only '/', and must not contain a route character (<c>{</c>, <c>}</c>, <c>*</c> or <c>?</c>) or white space.</param>
    /// <returns>The fallback's conventions, so the answer can be changed, for example to allow an anonymous caller.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is null, blank or not a valid prefix.</exception>
    public static IEndpointConventionBuilder MapApiNotFound(this IEndpointRouteBuilder endpoints, string prefix)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var pattern = RoutePatternOf(prefix) + "/{**rest}";

        return endpoints.MapFallback(pattern, static async context =>
        {
            var options = context.RequestServices.GetRequiredService<ProblemResponseOptions>();
            var problem = new Problem
            {
                Type = ProblemTypes.For(404),
                Title = ProblemResponses.TitlesOf(context).Title(404, ErrorCodes.EndpointNotFound),
                Status = 404,
                Detail = options.EndpointNotFoundDetail(context),
                Instance = context.Request.Path.Value,
                Code = ErrorCodes.EndpointNotFound,
                TraceId = ProblemResponses.TraceIdOf(context),
            };
            await ProblemResponses.WriteAsync(context, problem, context.RequestAborted);
        }).AllowAnonymous();
    }

    private static string RoutePatternOf(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix, nameof(prefix));
        if (!prefix.StartsWith('/'))
        {
            throw new ArgumentException($"The prefix '{prefix}' must start with '/', such as \"/api\".", nameof(prefix));
        }

        var segments = prefix.Trim('/');
        if (segments.Length == 0)
        {
            throw new ArgumentException("The prefix must name at least one segment. '/' alone would match every route.", nameof(prefix));
        }

        if (segments.Any(c => c is '{' or '}' or '*' or '?' || char.IsWhiteSpace(c)))
        {
            throw new ArgumentException($"The prefix '{prefix}' contains a route character or white space.", nameof(prefix));
        }

        return segments;
    }
}
