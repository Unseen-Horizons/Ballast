// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballast.AspNetCore;

/// <summary>Puts the problem answers into the request pipeline.</summary>
public static class ProblemApplicationExtensions
{
    /// <summary>
    /// Adds the trace reference to every response and to the log scope of every request, and turns an exception into its problem
    /// answer. Place it after the forwarded headers and before the rate limiter, the authentication and the static files, so their
    /// failures are answered as problems too.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="ProblemServiceExtensions.AddProblemResponses"/> was not called.</exception>
    public static IApplicationBuilder UseProblemResponses(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.ApplicationServices.GetService<ProblemResponseOptions>() is null)
        {
            throw new InvalidOperationException("UseProblemResponses needs AddProblemResponses in the service collection. Call it before Build().");
        }

        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ballast.AspNetCore.Trace");
        app.Use(async (context, next) =>
        {
            var traceId = ProblemResponses.TraceIdOf(context);
            if (!context.Response.HasStarted)
            {
                context.Response.Headers[ProblemResponses.TraceIdHeader] = traceId;
            }

            using var scope = logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId });
            await next();
        });

        app.UseExceptionHandler();
        return app;
    }
}
