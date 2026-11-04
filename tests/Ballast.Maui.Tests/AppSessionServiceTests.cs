// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.Maui.AppAttestation;

namespace Ballast.Maui.Tests;

/// <summary>The app's session with the server: when it attests, how often, and what it does when that fails.</summary>
public class AppSessionServiceTests
{
    private static (AppSessionService Service, FakeServer Server, FakeAttestation Attestation, StepClock Clock) Make()
    {
        var server = new FakeServer();
        var attestation = new FakeAttestation();
        var clock = new StepClock();
        return (new AppSessionService(attestation, server.Client(), () => "install-0000000001", clock), server, attestation, clock);
    }

    [Fact]
    public async Task The_first_call_attests_once_and_later_calls_reuse_the_session()
    {
        var (service, server, attestation, _) = Make();

        var first = await service.GetTokenAsync();
        var second = await service.GetTokenAsync();
        var third = await service.GetTokenAsync();

        Assert.Equal("session-1", first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(1, attestation.Calls);
        Assert.Equal(1, server.Count("/app/session"));
    }

    [Fact]
    public async Task The_server_is_asked_with_the_platform_the_install_the_nonce_and_the_token()
    {
        var (service, server, attestation, _) = Make();

        await service.GetTokenAsync();

        var post = server.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/app/session");
        Assert.Contains("\"platform\":\"android\"", post.Body, StringComparison.Ordinal);
        Assert.Contains("\"installId\":\"install-0000000001\"", post.Body, StringComparison.Ordinal);
        Assert.Contains($"\"nonce\":\"{attestation.Nonces.Single()}\"", post.Body, StringComparison.Ordinal);
        Assert.Contains("\"integrityToken\":\"play-token\"", post.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_concurrent_callers_share_one_attestation()
    {
        var (service, server, attestation, _) = Make();

        var tokens = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.GetTokenAsync()));

        Assert.All(tokens, t => Assert.Equal("session-1", t));
        Assert.Equal(1, attestation.Calls);
        Assert.Equal(1, server.Count("/app/session"));
        Assert.Equal(1, server.Count("/app/session/nonce"));
    }

    [Fact]
    public async Task A_caller_arriving_while_the_session_is_stored_never_sees_the_token_without_its_expiry()
    {
        // The fast path reads the session without the gate, and storing a new session reads the clock for its expiry. A caller
        // arriving at exactly that read used to find the new token beside the old default expiry, and DateTimeOffset.MinValue -
        // RenewBefore threw. It must see no session yet, wait, and get the new one.
        var (service, server, attestation, clock) = Make();
        Task<string?>? arrivedMidStore = null;
        server.OnSessionIssued = () => clock.OnNextRead = () => arrivedMidStore = service.GetTokenAsync();

        var token = await service.GetTokenAsync();

        Assert.NotNull(arrivedMidStore);
        Assert.Equal("session-1", token);
        Assert.Equal("session-1", await arrivedMidStore);
        Assert.Equal(1, attestation.Calls);
    }

    [Fact]
    public async Task The_session_is_renewed_five_minutes_before_it_ends()
    {
        var (service, server, attestation, clock) = Make();
        Assert.Equal("session-1", await service.GetTokenAsync());

        // Four hours short of the limit, nothing happens.
        clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal("session-1", await service.GetTokenAsync());
        Assert.Equal(1, attestation.Calls);

        // Within five minutes of the end it is replaced.
        clock.Advance(TimeSpan.FromMinutes(56));
        Assert.Equal("session-2", await service.GetTokenAsync());
        Assert.Equal(2, attestation.Calls);
        Assert.Equal(2, server.SessionsIssued);
    }

    [Fact]
    public async Task A_session_that_has_ended_is_replaced_not_reused()
    {
        var (service, _, attestation, clock) = Make();
        await service.GetTokenAsync();

        clock.Advance(TimeSpan.FromHours(30));

        Assert.Equal("session-2", await service.GetTokenAsync());
        Assert.Equal(2, attestation.Calls);
    }

    [Fact]
    public async Task A_failed_attestation_gives_no_session_and_is_not_repeated_straight_away()
    {
        var (service, server, attestation, clock) = Make();
        server.SessionStatus = HttpStatusCode.Forbidden;

        Assert.Null(await service.GetTokenAsync());
        Assert.Null(await service.GetTokenAsync());
        Assert.Null(await service.GetTokenAsync());

        Assert.Equal(1, attestation.Calls);
        Assert.Equal(AppSessionFailureKind.Refused, service.LastFailure!.Kind);
        Assert.Equal("APP_ATTESTATION_FAILED", service.LastFailure.Code);

        // After the backoff it tries again, and a success clears the failure.
        server.SessionStatus = HttpStatusCode.OK;
        clock.Advance(AppSessionService.FailureBackoff + TimeSpan.FromSeconds(1));
        Assert.Equal("session-1", await service.GetTokenAsync());
        Assert.Null(service.LastFailure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, AppSessionFailureKind.Refused, "APP_ATTESTATION_FAILED")]
    [InlineData(HttpStatusCode.ServiceUnavailable, AppSessionFailureKind.Unavailable, "APP_ATTESTATION_UNAVAILABLE")]
    [InlineData(HttpStatusCode.BadRequest, AppSessionFailureKind.Other, "VALIDATION_FAILED")]
    public async Task A_refusal_is_told_apart_from_an_outage(HttpStatusCode status, AppSessionFailureKind kind, string code)
    {
        var (service, server, _, _) = Make();
        server.SessionStatus = status;
        server.SessionCode = code;

        Assert.Null(await service.GetTokenAsync());

        Assert.Equal(kind, service.LastFailure!.Kind);
        Assert.Equal(code, service.LastFailure.Code);
    }

    [Fact]
    public async Task A_dead_network_is_reported_as_unreachable_not_as_a_refusal()
    {
        var (service, server, _, _) = Make();
        server.Unreachable = true;

        Assert.Null(await service.GetTokenAsync());

        Assert.Equal(AppSessionFailureKind.Unreachable, service.LastFailure!.Kind);
    }

    [Fact]
    public async Task A_platform_with_no_token_still_asks_the_server_which_decides()
    {
        var (service, server, attestation, _) = Make();
        attestation.Token = null;

        // Development servers issue an unattested session; here the fake does.
        Assert.Equal("session-1", await service.GetTokenAsync());

        var post = server.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/app/session");
        Assert.Contains("\"integrityToken\":null", post.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_renewal_fails_a_session_that_has_not_ended_is_still_used()
    {
        var (service, server, _, clock) = Make();
        await service.GetTokenAsync();

        server.SessionStatus = HttpStatusCode.ServiceUnavailable;
        clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(57));

        // Inside the renewal window but not past the end: Google being down must not take a working session away.
        Assert.Equal("session-1", await service.GetTokenAsync());
        Assert.Equal(AppSessionFailureKind.Unavailable, service.LastFailure!.Kind);
    }

    [Fact]
    public async Task A_rejected_session_is_replaced_even_during_the_backoff()
    {
        var (service, server, attestation, _) = Make();
        var stale = await service.GetTokenAsync();

        var fresh = await service.RefreshAsync(stale);

        Assert.Equal("session-2", fresh);
        Assert.Equal(2, attestation.Calls);
        Assert.Equal(2, server.Count("/app/session"));
    }

    [Fact]
    public async Task A_session_someone_else_already_replaced_is_not_attested_for_again()
    {
        var (service, _, attestation, _) = Make();
        var stale = await service.GetTokenAsync();
        await service.RefreshAsync(stale);
        Assert.Equal(2, attestation.Calls);

        // A second call that was rejected with the same old token finds the new one already there.
        var again = await service.RefreshAsync(stale);

        Assert.Equal("session-2", again);
        Assert.Equal(2, attestation.Calls);
    }

    [Fact]
    public async Task Forcing_a_renewal_attests_again_whatever_the_session_looks_like()
    {
        var (service, _, attestation, _) = Make();
        await service.GetTokenAsync();

        Assert.Equal("session-2", await service.ForceRenewAsync());
        Assert.Equal(2, attestation.Calls);
    }

    [Fact]
    public async Task A_web_ticket_is_asked_for_with_the_session()
    {
        var (service, server, _, _) = Make();
        server.Api = (_, _) => FakeServer.Json(HttpStatusCode.OK, "{\"ticket\":\"ticket-abc\"}");

        var result = await service.GetWebTicketAsync();

        Assert.Equal("ticket-abc", result.Ticket);
        Assert.Null(result.Failure);
        Assert.Equal("session-1", server.Requests.Single(r => r.Path == "/app/session/web-ticket").Session);
    }

    [Fact]
    public async Task A_web_ticket_refused_for_the_session_attests_again_once_and_tries_again()
    {
        var (service, server, attestation, _) = Make();
        server.Api = (_, session) => session == "session-1"
            ? FakeServer.Problem(HttpStatusCode.Unauthorized, "APP_SESSION_REQUIRED", "App verification required")
            : FakeServer.Json(HttpStatusCode.OK, "{\"ticket\":\"ticket-2\"}");

        var result = await service.GetWebTicketAsync();

        Assert.Equal("ticket-2", result.Ticket);
        Assert.Equal(2, attestation.Calls);
        Assert.Equal(2, server.Count("/app/session/web-ticket"));
    }

    [Fact]
    public async Task No_web_ticket_without_a_session_says_why()
    {
        var (service, server, _, _) = Make();
        server.SessionStatus = HttpStatusCode.Forbidden;

        var result = await service.GetWebTicketAsync();

        Assert.Null(result.Ticket);
        Assert.Equal(AppSessionFailureKind.Refused, result.Failure!.Kind);
        Assert.Equal(0, server.Count("/app/session/web-ticket"));
    }
}
