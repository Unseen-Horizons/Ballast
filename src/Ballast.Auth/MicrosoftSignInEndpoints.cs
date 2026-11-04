// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Ballast.Auth;

/// <summary>The body of the one-time-code exchange.</summary>
/// <param name="Code">The code from the sign-in redirect.</param>
public sealed record MicrosoftCodeExchangeRequest(string Code);

/// <summary>
/// The three Microsoft sign-in routes, under a prefix (default <c>/auth/microsoft</c>):
/// <list type="bullet">
/// <item><description><c>GET {prefix}/start?login_hint=</c>: challenges Entra. An address at another company domain is refused before Entra is asked.</description></item>
/// <item><description><c>GET {prefix}/complete</c>: reads the handshake, decides it, and redirects to the sign-in page with a one-time code or an error.</description></item>
/// <item><description><c>POST {prefix}/exchange</c> with <c>{ "code": "..." }</c>: turns the code into the session (<see cref="IssuedSession"/>).</description></item>
/// </list>
/// A refusal is a redirect with <c>?error=</c> (and <c>&amp;ms=</c> for Microsoft's own code), never a message from Microsoft.
/// </summary>
public static class MicrosoftSignInEndpoints
{
    /// <summary>The route prefix the routes are mapped under by default.</summary>
    public const string DefaultRoutePrefix = "/auth/microsoft";

    /// <summary>The sign-in hint's item name in the challenge's properties.</summary>
    internal const string LoginHintItem = "login_hint";

    /// <summary>Maps the three routes.</summary>
    /// <typeparam name="TUser">The app's Identity user.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TRole">The app's Identity role.</typeparam>
    /// <typeparam name="TRefresh">The app's refresh token entity.</typeparam>
    /// <typeparam name="TContext">The app's database context.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="routePrefix">The prefix of the three routes. Default <c>/auth/microsoft</c>.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapBallastMicrosoftSignIn<TUser, TKey, TRole, TRefresh, TContext>(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = DefaultRoutePrefix)
        where TUser : Microsoft.AspNetCore.Identity.IdentityUser<TKey>, IAuthProfile, new()
        where TKey : IEquatable<TKey>
        where TRole : Microsoft.AspNetCore.Identity.IdentityRole<TKey>, new()
        where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
        where TContext : Microsoft.EntityFrameworkCore.DbContext
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);

        var prefix = routePrefix.TrimEnd('/');
        var completePath = prefix + "/complete";

        // The routes are thin: the decisions are MicrosoftSignInStart.Decide and MicrosoftSignInService.CompleteAsync(HttpContext), which an
        // app's own controller calls the same way.
        endpoints.MapGet(prefix + "/start", ([FromQuery(Name = "login_hint")] string? loginHint, MicrosoftSignInOptions microsoft) =>
        {
            var decision = MicrosoftSignInStart.Decide(microsoft, loginHint, completePath);
            if (decision.RedirectUrl is { } refused)
            {
                return Results.Redirect(refused);
            }

            return Results.Challenge(decision.Properties, [decision.Scheme]);
        }).AllowAnonymous();

        endpoints.MapGet(completePath, async (
            HttpContext http,
            MicrosoftSignInService<TUser, TKey, TRole, TRefresh, TContext> service,
            CancellationToken ct) =>
            Results.Redirect(await service.CompleteAsync(http, ct))).AllowAnonymous();

        endpoints.MapPost(prefix + "/exchange", async (
            MicrosoftCodeExchangeRequest request,
            MicrosoftSignInService<TUser, TKey, TRole, TRefresh, TContext> service,
            CancellationToken ct) =>
            Results.Ok(await service.ExchangeAsync(request.Code, ct))).AllowAnonymous();

        return endpoints;
    }
}
