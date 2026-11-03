// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.AppAttestation.Tests.Support;

/// <summary>
/// The bootstrap controller an app writes: it is thin and holds no rule, it passes values to <see cref="AppSessionManager"/> and writes
/// the answer. The library's tests serve this one so the thin shape is exercised, as an app's controller would use it.
/// </summary>
[ApiController]
[Route("app/session")]
[EnableRateLimiting("AppSession")]
public sealed class TestAppSessionController : ControllerBase
{
    private readonly AppSessionManager _manager;
    private readonly AppAttestationSetup _setup;

    public TestAppSessionController(AppSessionManager manager, AppAttestationSetup setup)
    {
        _manager = manager;
        _setup = setup;
    }

    [HttpGet("nonce")]
    [AllowAnonymous]
    public async Task<ActionResult<AppNonceResponse>> Nonce(CancellationToken ct) => Ok(await _manager.IssueNonceAsync(ct));

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<AppSessionResponse>> Create([FromBody] CreateAppSessionRequest request, CancellationToken ct) =>
        Ok(await _manager.CreateSessionAsync(request, ct));

    [HttpPost("web-ticket")]
    [Authorize(AuthenticationSchemes = AppAttestationDefaults.SchemeName)]
    public async Task<ActionResult<AppWebTicketResponse>> WebTicket(CancellationToken ct)
    {
        // The scheme has authenticated the caller, so a session is on the request.
        var session = AppSessionContext.Get(HttpContext)!;
        return Ok(await _manager.IssueWebTicketAsync(session, ct));
    }

    [HttpGet("web")]
    [AllowAnonymous]
    public async Task<IActionResult> Web([FromQuery] string? ticket, [FromQuery] string? next, CancellationToken ct)
    {
        var redemption = await _manager.RedeemWebTicketAsync(ticket, next, ct);
        AppSessionCookie.Write(Response, _setup, redemption);
        Response.Headers.CacheControl = "no-store";
        return LocalRedirect(redemption.RedirectTo);
    }
}

/// <summary>Data endpoints standing in for an API's: one gated, one open, one for signed-in users only.</summary>
[ApiController]
[Route("fake")]
[Authorize]
public sealed class TestGateController : ControllerBase
{
    [HttpGet("data"), AllowAnonymous, RequireAppSession, EnableRateLimiting("Tiny")]
    public IActionResult Data() => Ok(new { ok = true });

    [HttpGet("open"), AllowAnonymous]
    public IActionResult Open() => Ok(new { ok = true });

    [HttpGet("admin")]
    public IActionResult Admin() => Ok(new { ok = true });
}

/// <summary>Only the listed controller types, so the host serves the test endpoints and nothing else.</summary>
internal sealed class OnlyTypesPart(params Type[] types) : ApplicationPart, IApplicationPartTypeProvider
{
    public override string Name => "OnlyTypes";

    public IEnumerable<TypeInfo> Types => types.Select(t => t.GetTypeInfo());
}

/// <summary>
/// A stand-in for an app's user-token scheme: it accepts a bearer token that is a user JWT (its own key and audience) and signs the
/// caller in with its claims. The real bearer pipeline is the app's and is not part of this library.
/// </summary>
public sealed class TestBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestBearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(header["Bearer ".Length..], new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = AppAttestationTestHost.UserIssuer,
            ValidateAudience = true,
            ValidAudience = AppAttestationTestHost.UserAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AppAttestationTestHost.UserKey)),
        });

        if (!result.IsValid)
            return AuthenticateResult.Fail("The user token was not accepted.");

        var identity = new ClaimsIdentity(result.ClaimsIdentity.Claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
