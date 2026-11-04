// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation;

/// <summary>
/// Runs after authentication and before the rate limiter. For every request that carries a session it validates it once and parks
/// it on the request, so the limiter can partition on it and the session scheme does not validate twice. Then, for an endpoint
/// marked <see cref="RequireAppSessionAttribute"/>, a request with neither a valid session nor a signed-in user is refused with 401
/// (<see cref="AppAttestationSetup.SessionRequiredCode"/>) when <see cref="AppAttestationOptions.Enforce"/> is on, and only logged when
/// it is off.
/// <para>
/// A signed-in user is whoever the existing bearer scheme authenticated, so an admin console keeps reading everything. The session is
/// never put on <c>HttpContext.User</c>: nothing that asks "is there a user" may mistake an install for a person.
/// </para>
/// </summary>
public sealed class AppSessionGateMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next step of the pipeline.</param>
    public AppSessionGateMiddleware(RequestDelegate next) { _next = next; }

    /// <summary>Validates the session, if one is presented, and enforces or logs the access rule for the endpoint.</summary>
    /// <param name="context">The request.</param>
    /// <param name="validator">The session validator.</param>
    /// <param name="setup">The setup, which names the header, the cookie and the refusal's code.</param>
    /// <param name="options">The attestation options. <see cref="AppAttestationOptions.Enforce"/> is read here.</param>
    /// <param name="wouldRefuse">The log of calls that would have been refused.</param>
    /// <param name="logger">The logger for a presented session that is not accepted (debug) and for refusals (information).</param>
    /// <returns>A task that completes when the rest of the pipeline, or the refusal, is done.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        AppSessionValidator validator,
        AppAttestationSetup setup,
        IOptions<AppAttestationOptions> options,
        WouldRefuseLog wouldRefuse,
        ILogger<AppSessionGateMiddleware> logger)
    {
        var token = AppSessionContext.ReadToken(context.Request, setup);
        if (token is not null)
        {
            var validation = await validator.ValidateAsync(token);
            if (validation.Session is { } info)
                AppSessionContext.Set(context, new AppSessionFeature(info, token));
            else
                logger.LogDebug("A presented app session was not accepted: {Failure}", validation.Failure);
        }

        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<RequireAppSessionAttribute>() is not null
            && AppSessionContext.Get(context) is null
            && context.User.Identity?.IsAuthenticated != true)
        {
            var route = $"{context.Request.Method} {(endpoint as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value}";
            var partition = AppSessionContext.RateLimitKey(context);

            if (options.Value.Enforce)
            {
                logger.LogInformation("App session required: refused {Route} from {Caller}", route, AppSessionContext.LogLabel(partition));
                await AppSessionContext.WriteRequiredAsync(context, setup);
                return;
            }

            wouldRefuse.Record(route, partition);
        }

        await _next(context);
    }
}
