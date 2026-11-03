// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

using Ballast.AppAttestation.Tests.Support;

using Microsoft.AspNetCore.Http;

namespace Ballast.AppAttestation.Tests;

/// <summary>
/// The access rule on the data endpoints, through the real gate, scheme and limiter on a test host: log-only lets everything through and
/// says what it would refuse; enforced refuses a caller with neither a session nor a user, and lets the app, a signed-in user and the
/// open routes through.
/// </summary>
public class AppSessionGateTests
{
    private static HttpRequestMessage Get(string url, string? session = null, string? bearer = null, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (session is not null)
            request.Headers.Add(AppAttestationTestHost.Header, session);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (cookie is not null)
            request.Headers.Add("Cookie", $"{AppAttestationTestHost.Cookie}={cookie}");
        return request;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    // ---- log-only (the default everywhere) ------------------------------------------------------

    [Fact]
    public async Task Log_only_lets_an_anonymous_call_through_and_logs_it()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: false);

        var response = await host.Client.SendAsync(Get("/fake/data"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(host.Logs.Lines, l => l.Contains("would be required (log-only)") && l.Contains("GET fake/data") && l.Contains("ip:"));
    }

    // The rate-limit partition can be a raw client address, and an address is personal data: the lines say who from only as a short
    // keyed fingerprint, enough to see that one caller repeats and not enough to name them.

    [Fact]
    public async Task Log_only_never_writes_the_callers_address()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: false, remoteIp: "203.0.113.9");

        await host.Client.SendAsync(Get("/fake/data"));

        var line = Assert.Single(host.Logs.Lines, l => l.Contains("would be required (log-only)"));
        Assert.DoesNotContain("203.0.113.9", line);
        Assert.Matches(@"from ip:[0-9a-f]{12}$", line);
    }

    [Fact]
    public async Task Enforced_refusals_never_write_the_callers_address()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true, remoteIp: "203.0.113.9");

        await host.Client.SendAsync(Get("/fake/data"));

        var line = Assert.Single(host.Logs.Lines, l => l.Contains("App session required: refused"));
        Assert.DoesNotContain("203.0.113.9", line);
        Assert.Matches(@"from ip:[0-9a-f]{12}$", line);
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains("203.0.113.9"));
    }

    [Fact]
    public void The_log_label_is_a_stable_fingerprint_that_keeps_the_kind_and_hides_the_value()
    {
        var one = AppSessionContext.LogLabel("ip:203.0.113.9");

        Assert.Matches(@"^ip:[0-9a-f]{12}$", one);
        Assert.DoesNotContain("203.0.113.9", one);
        Assert.Equal(one, AppSessionContext.LogLabel("ip:203.0.113.9"));
        Assert.NotEqual(one, AppSessionContext.LogLabel("ip:203.0.113.10"));
        Assert.Matches(@"^app:[0-9a-f]{12}$", AppSessionContext.LogLabel("app:sid-1"));
        Assert.Matches(@"^user:[0-9a-f]{12}$", AppSessionContext.LogLabel("user:u1"));
        Assert.Matches(@"^partition:[0-9a-f]{12}$", AppSessionContext.LogLabel("no-kind"));
    }

    [Fact]
    public async Task Log_only_writes_one_line_a_minute_per_route_and_caller()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: false);

        for (var i = 0; i < 5; i++)
            await host.Client.SendAsync(Get("/fake/data"));
        Assert.Single(host.Logs.Lines, l => l.Contains("would be required (log-only)"));

        host.Clock.Advance(TimeSpan.FromSeconds(61));
        await host.Client.SendAsync(Get("/fake/data"));
        Assert.Equal(2, host.Logs.Lines.Count(l => l.Contains("would be required (log-only)")));
    }

    [Fact]
    public async Task Log_only_writes_nothing_for_a_caller_that_has_a_session()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: false);
        var session = await host.CreateSessionAsync();

        var response = await host.Client.SendAsync(Get("/fake/data", session: session));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains("would be required"));
    }

    // ---- enforced -------------------------------------------------------------------------------

    [Fact]
    public async Task Enforced_refuses_an_anonymous_call_with_401_app_session_required()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);

        var response = await host.Client.SendAsync(Get("/fake/data"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("APP_SESSION_REQUIRED", await CodeAsync(response));
    }

    [Fact]
    public async Task The_refusal_carries_the_title_and_detail_the_setup_gives()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);

        var response = await host.Client.SendAsync(Get("/fake/data"));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("App verification required", body.GetProperty("title").GetString());
        Assert.Equal("This endpoint answers only the example app. Open it from the app, or sign in.", body.GetProperty("detail").GetString());
        Assert.Equal("/fake/data", body.GetProperty("instance").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.2", body.GetProperty("type").GetString());
        Assert.Equal(401, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        Assert.Equal("application/problem+json; charset=utf-8", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Enforced_lets_an_app_session_in_by_header()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);
        var session = await host.CreateSessionAsync();

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/fake/data", session: session))).StatusCode);
    }

    [Fact]
    public async Task Enforced_lets_an_app_session_in_by_cookie()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);
        var session = await host.CreateSessionAsync();

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/fake/data", cookie: session))).StatusCode);
    }

    [Fact]
    public async Task Enforced_lets_a_signed_in_user_in()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/fake/data", bearer: AppAttestationTestHost.UserJwt()))).StatusCode);
    }

    [Fact]
    public async Task Enforced_refuses_an_expired_session_so_the_app_attests_again()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);
        var session = await host.CreateSessionAsync();

        host.Clock.Advance(TimeSpan.FromHours(25));

        var response = await host.Client.SendAsync(Get("/fake/data", session: session));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("APP_SESSION_REQUIRED", await CodeAsync(response));
    }

    [Fact]
    public async Task Enforced_refuses_a_user_token_presented_as_a_session()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);

        var response = await host.Client.SendAsync(Get("/fake/data", session: AppAttestationTestHost.UserJwt()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enforced_refuses_a_made_up_session()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(Get("/fake/data", session: "a.b.c"))).StatusCode);
    }

    [Fact]
    public async Task Enforced_leaves_routes_that_are_not_data_calls_alone()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);

        // An open route (login, refresh and health are of this kind) and the bootstrap itself.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/fake/open"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/app/session/nonce"))).StatusCode);
    }

    [Fact]
    public async Task A_route_that_asks_for_a_signed_in_user_still_refuses_an_app_session()
    {
        // An install is not a person: it does not pass [Authorize].
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true);
        var session = await host.CreateSessionAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(Get("/fake/admin", session: session))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/fake/admin", bearer: AppAttestationTestHost.UserJwt()))).StatusCode);
    }

    [Fact]
    public async Task An_enforced_gate_with_a_setup_that_names_other_codes_answers_those_codes()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true, adjustSetup: setup => setup.SessionRequiredCode = "SAMPLE_REQUIRED");

        var response = await host.Client.SendAsync(Get("/fake/data"));

        Assert.Equal("SAMPLE_REQUIRED", await CodeAsync(response));
    }

    // ---- rate limit partitions ------------------------------------------------------------------

    [Fact]
    public async Task Two_sessions_from_one_address_are_limited_separately()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: true, tinyLimit: 2);
        var first = await host.CreateSessionAsync(installId: "install-0000000001");
        var second = await host.CreateSessionAsync(installId: "install-0000000002");

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/fake/data", session: first))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/fake/data", session: first))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.SendAsync(Get("/fake/data", session: first))).StatusCode);

        // The second install, same address, has its own bucket.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("/fake/data", session: second))).StatusCode);
    }

    [Fact]
    public void The_partition_is_the_session_then_the_user_then_the_address()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        Assert.Equal("ip:203.0.113.9", AppSessionContext.RateLimitKey(context));

        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "Bearer"));
        Assert.Equal("user:u1", AppSessionContext.RateLimitKey(context));

        AppSessionContext.Set(context, new AppSessionFeature(new AppSessionInfo("sid-1", "play", "android", DateTimeOffset.UtcNow.AddHours(1)), "token"));
        Assert.Equal("app:sid-1", AppSessionContext.RateLimitKey(context));
    }

    [Fact]
    public void A_session_is_read_from_the_header_then_the_cookie_under_the_names_the_setup_gives()
    {
        var setup = new AppAttestationSetup { HeaderName = "X-Sample", CookieName = "sample_app" };

        var fromHeader = new DefaultHttpContext();
        fromHeader.Request.Headers["X-Sample"] = " token-a ";
        Assert.Equal("token-a", AppSessionContext.ReadToken(fromHeader.Request, setup));

        var fromCookie = new DefaultHttpContext();
        fromCookie.Request.Headers["Cookie"] = "sample_app=token-b";
        Assert.Equal("token-b", AppSessionContext.ReadToken(fromCookie.Request, setup));

        var neither = new DefaultHttpContext();
        neither.Request.Headers["X-App-Session"] = "wrong-header";
        Assert.Null(AppSessionContext.ReadToken(neither.Request, setup));
    }
}
