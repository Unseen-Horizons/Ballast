// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Net;
using System.Text.Json;

using Ballast.AppAttestation.Tests.Support;

namespace Ballast.AppAttestation.Tests;

/// <summary>
/// The ticket that carries a session from the app into its web view, and the cookie it becomes: single use, sixty seconds, never an open
/// redirect, and an HttpOnly, Secure, SameSite=Strict cookie for exactly the session's remaining lifetime.
/// </summary>
public class AppSessionWebTicketTests
{
    private const string Prefix = AppAttestationTestHost.Prefix;

    private static async Task<string> TicketAsync(AppAttestationTestHost host, string session)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/app/session/web-ticket");
        request.Headers.Add(AppAttestationTestHost.Header, session);
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("ticket").GetString()!;
    }

    private static Task<HttpResponseMessage> RedeemAsync(AppAttestationTestHost host, string? ticket, string next) =>
        host.Client.GetAsync($"/app/session/web?ticket={Uri.EscapeDataString(ticket ?? string.Empty)}&next={Uri.EscapeDataString(next)}");

    private static string? SetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.FirstOrDefault(v => v.StartsWith(AppAttestationTestHost.Cookie + "=", StringComparison.Ordinal)) : null;

    [Fact]
    public async Task A_ticket_needs_a_session_to_be_issued()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: false);

        var response = await host.Client.PostAsync("/app/session/web-ticket", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_ticket_is_refused_to_an_invalid_session()
    {
        await using var host = await AppAttestationTestHost.StartAsync(enforce: false);
        var request = new HttpRequestMessage(HttpMethod.Post, "/app/session/web-ticket");
        request.Headers.Add(AppAttestationTestHost.Header, "a.b.c");

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Redeeming_a_ticket_sets_the_cookie_and_redirects_to_the_prefixed_path()
    {
        await using var host = await AppAttestationTestHost.StartAsync();
        var session = await host.CreateSessionAsync();
        var ticket = await TicketAsync(host, session);

        var response = await RedeemAsync(host, ticket, "/view/public.html?lang=hr&host=app");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/view/public.html?lang=hr&host=app", response.Headers.Location!.OriginalString);

        var cookie = SetCookie(response);
        Assert.NotNull(cookie);

        // The cookie holds the same session's token.
        Assert.StartsWith($"{AppAttestationTestHost.Cookie}={session}", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_cookie_lives_exactly_as_long_as_the_session_has_left()
    {
        await using var host = await AppAttestationTestHost.StartAsync();
        var session = await host.CreateSessionAsync();

        // Five hours into a 24-hour session: nineteen hours left, not twenty-four.
        host.Clock.Advance(TimeSpan.FromHours(5));
        var ticket = await TicketAsync(host, session);
        var response = await RedeemAsync(host, ticket, "/view/public.html");

        var cookie = SetCookie(response)!;
        var maxAge = cookie.Split(';').Select(p => p.Trim()).Single(p => p.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal((19 * 3600).ToString(CultureInfo.InvariantCulture), maxAge["max-age=".Length..]);
    }

    [Fact]
    public async Task A_ticket_works_once()
    {
        await using var host = await AppAttestationTestHost.StartAsync();
        var ticket = await TicketAsync(host, await host.CreateSessionAsync());

        Assert.NotNull(SetCookie(await RedeemAsync(host, ticket, "/view/public.html")));

        var again = await RedeemAsync(host, ticket, "/view/public.html");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Null(SetCookie(again));
    }

    [Fact]
    public async Task A_ticket_expires_after_sixty_seconds()
    {
        await using var host = await AppAttestationTestHost.StartAsync();
        var ticket = await TicketAsync(host, await host.CreateSessionAsync());

        host.Clock.Advance(TimeSpan.FromSeconds(61));

        var response = await RedeemAsync(host, ticket, "/view/public.html");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(SetCookie(response));
    }

    [Fact]
    public async Task A_ticket_that_was_never_issued_redirects_without_a_cookie_and_says_so_in_the_log()
    {
        await using var host = await AppAttestationTestHost.StartAsync();

        var response = await RedeemAsync(host, "made-up", "/view/public.html");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(SetCookie(response));
        Assert.Contains(host.Logs.Lines, l => l.Contains("Web ticket not redeemed"));
    }

    [Theory]
    [InlineData("https://evil.example/view/")]
    [InlineData("//evil.example/view/")]
    [InlineData("/\\evil.example")]
    [InlineData("/admin/index.html")]
    [InlineData("/viewx/public.html")]
    [InlineData("/view")]
    [InlineData("view/public.html")]
    [InlineData("/view/../admin/index.html")]
    [InlineData("/view/%2e%2e/admin/")]
    [InlineData("/view/public.html\r\nSet-Cookie: x=1")]
    [InlineData("")]
    public async Task A_next_that_is_not_a_relative_prefixed_path_is_400_and_spends_nothing(string next)
    {
        await using var host = await AppAttestationTestHost.StartAsync();
        var ticket = await TicketAsync(host, await host.CreateSessionAsync());

        var bad = await RedeemAsync(host, ticket, next);

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Null(bad.Headers.Location);
        Assert.Null(SetCookie(bad));

        // The ticket was not spent by the refused attempt.
        Assert.NotNull(SetCookie(await RedeemAsync(host, ticket, "/view/public.html")));
    }

    [Fact]
    public async Task A_next_under_a_prefix_the_setup_does_not_name_is_400()
    {
        await using var host = await AppAttestationTestHost.StartAsync();
        var ticket = await TicketAsync(host, await host.CreateSessionAsync());

        var response = await RedeemAsync(host, ticket, "/map/public.html");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_missing_next_is_400()
    {
        await using var host = await AppAttestationTestHost.StartAsync();

        var response = await host.Client.GetAsync("/app/session/web?ticket=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void The_redirect_rule_accepts_a_page_with_a_query_and_nothing_else()
    {
        Assert.True(AppSessionManager.IsSafeRedirectPath("/view/public.html?lang=hr&host=app&flagged=a%2Cb&planfromlabel=Sample%20place", Prefix));
        Assert.True(AppSessionManager.IsSafeRedirectPath("/view/", Prefix));
        Assert.False(AppSessionManager.IsSafeRedirectPath(null, Prefix));
        Assert.False(AppSessionManager.IsSafeRedirectPath("/view/" + new string('a', 9000), Prefix));
    }

    [Fact]
    public void The_redirect_rule_takes_the_prefix_it_is_given()
    {
        Assert.True(AppSessionManager.IsSafeRedirectPath("/map/public.html", "/map/"));
        Assert.False(AppSessionManager.IsSafeRedirectPath("/map/public.html", Prefix));
    }

    [Theory]
    [InlineData("/view/ü")]
    [InlineData("/view/a b")]
    [InlineData("/view/a\u007f")]
    public void The_redirect_rule_refuses_non_ascii_space_and_del(string next) =>
        Assert.False(AppSessionManager.IsSafeRedirectPath(next, Prefix));

    [Theory]
    [InlineData("/view/%2e%2E/admin/x")]
    [InlineData("/view/%2E%2e/x")]
    public void The_redirect_rule_refuses_an_encoded_dot_segment_in_any_case(string next) =>
        Assert.False(AppSessionManager.IsSafeRedirectPath(next, Prefix));

    [Theory]
    [InlineData("/view/a%2eb/x")]
    [InlineData("/view/public.html?next=%2e%2E")]
    public void The_redirect_rule_keeps_an_encoded_dot_that_is_not_a_dot_segment(string next) =>
        Assert.True(AppSessionManager.IsSafeRedirectPath(next, Prefix));

    [Fact]
    public void A_cookie_is_written_only_for_a_redemption_that_has_a_token()
    {
        var setup = new AppAttestationSetup { CookieName = AppAttestationTestHost.Cookie };
        var response = new Microsoft.AspNetCore.Http.DefaultHttpContext().Response;
        var expires = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

        Assert.False(AppSessionCookie.Write(response, setup, new WebTicketRedemption("/view/x", null, null, null)));
        Assert.True(AppSessionCookie.Write(response, setup, new WebTicketRedemption("/view/x", "token-c", expires, TimeSpan.FromHours(1))));

        var cookie = response.Headers["Set-Cookie"].ToString();
        Assert.StartsWith($"{AppAttestationTestHost.Cookie}=token-c", cookie, StringComparison.Ordinal);
        Assert.Contains("max-age=3600", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
