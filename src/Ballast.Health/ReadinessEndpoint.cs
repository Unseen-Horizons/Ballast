// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ballast.Health;

/// <summary>
/// The options of the readiness route: the checks tagged <see cref="ReadyTag"/>, answered 200 when Healthy, 200 when Degraded
/// (a capped gate that is ready anyway) and 503 when Unhealthy. The body names each check and its status, and nothing else:
/// no description, exception, data or duration, because this route is anonymous.
/// </summary>
public static class ReadinessEndpoint
{
    /// <summary>
    /// The tag a check carries to be part of readiness. Every check this library registers carries it, and so must any check an
    /// outside library or the app adds to readiness. A check with the tag takes part in the readiness route, the readiness log and
    /// the startup hold. A check without it takes part in none of them.
    /// </summary>
    public const string ReadyTag = "ready";

    /// <summary>The health check options for the readiness route: only the <see cref="ReadyTag"/> checks, with the status codes above.</summary>
    /// <returns>The options to pass to <c>MapHealthChecks</c>.</returns>
    public static HealthCheckOptions Options() => new()
    {
        Predicate = check => check.Tags.Contains(ReadyTag),
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        },
        ResponseWriter = WriteAsync,
    };

    private static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        // A probe must see the readiness as it is now, never an answer a cache kept.
        context.Response.Headers.CacheControl = "no-store";
        context.RequestServices.GetRequiredService<ReadinessLog>().Observe(context, report);

        var checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString(), StringComparer.Ordinal);
        await context.Response.WriteAsJsonAsync(new ReadinessBody(report.Status.ToString(), checks), context.RequestAborted);
    }

    /// <summary>The overall status, and each check's status by name.</summary>
    private sealed record ReadinessBody(string Status, IReadOnlyDictionary<string, string> Checks);
}
