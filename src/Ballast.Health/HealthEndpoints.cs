// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ballast.Health;

/// <summary>
/// The health routes. Each is anonymous and exempt from rate limiting, because a probe is cheap by construction and a platform
/// must always be able to ask. <see cref="MapBallastHealth"/> maps the three default routes in one call.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Maps the build route (<c>/health</c>), the liveness route (<c>/health/live</c>) and the readiness route (<c>/health/ready</c>),
    /// and starts the warm-up once the application has started.
    /// </summary>
    /// <param name="endpoints">The application. Required.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapBallastHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapBuildInfo();
        endpoints.MapLiveness();
        endpoints.MapReadiness();
        return endpoints;
    }

    /// <summary>
    /// Maps the build route: a JSON body with the commit, the build time, the start time and the current time. It answers 200 while
    /// the process runs, and reports nothing from the checks.
    /// </summary>
    /// <param name="endpoints">The application. Required.</param>
    /// <param name="path">The route. Default <c>/health</c>. It must not be blank.</param>
    /// <returns>The route's conventions, for more of them.</returns>
    public static IEndpointConventionBuilder MapBuildInfo(this IEndpointRouteBuilder endpoints, string path = "/health")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return endpoints.MapGet(path, (BuildInfo build, HealthStart start, TimeProvider time) =>
                Results.Ok(HealthBody.Create(build, start.StartedUtc, time.GetUtcNow().UtcDateTime)))
            .AllowAnonymous()
            .DisableRateLimiting();
    }

    /// <summary>
    /// Maps the liveness route. It answers 200 while the process runs, and no check is consulted, so a restart loop cannot be
    /// caused by a database blip.
    /// </summary>
    /// <param name="endpoints">The application. Required.</param>
    /// <param name="path">The route. Default <c>/health/live</c>. It must not be blank.</param>
    /// <returns>The route's conventions, for more of them.</returns>
    public static IEndpointConventionBuilder MapLiveness(this IEndpointRouteBuilder endpoints, string path = "/health/live")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return endpoints.MapHealthChecks(path, new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false,
        })
            .AllowAnonymous()
            .DisableRateLimiting();
    }

    /// <summary>
    /// Maps the readiness route (<see cref="ReadinessEndpoint"/>): 200 when Healthy or Degraded, 503 when Unhealthy, with each
    /// check's status in the body. It also starts the warm-up once the application has started, so that the warm-up begins whether
    /// or not the startup hold is on. Starting it twice does nothing.
    /// </summary>
    /// <param name="endpoints">The application. Required.</param>
    /// <param name="path">The route. Default <c>/health/ready</c>. It must not be blank.</param>
    /// <returns>The route's conventions, for more of them.</returns>
    public static IEndpointConventionBuilder MapReadiness(this IEndpointRouteBuilder endpoints, string path = "/health/ready")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var builder = endpoints.MapHealthChecks(path, ReadinessEndpoint.Options())
            .AllowAnonymous()
            .DisableRateLimiting();

        // The warm-up begins once the port is open. The hold starts it earlier when it is on. Either way the hosted-service order
        // of the application is unchanged, because the warm-up is not a hosted service.
        var services = endpoints.ServiceProvider;
        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => services.GetRequiredService<StartupWarmup>().Begin());
        return builder;
    }
}
