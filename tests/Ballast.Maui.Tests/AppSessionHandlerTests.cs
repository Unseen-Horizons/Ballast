// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;
using System.Text.Json;

using Ballast.Maui.AppAttestation;

namespace Ballast.Maui.Tests;

/// <summary>
/// The handler on the app's server client: it adds the session, and answers the server refusing it by attesting again once and
/// replaying. A failure to attest is a visible error, never an offline substitute.
/// </summary>
public class AppSessionHandlerTests
{
    private static (HttpClient Client, FakeServer Server, FakeAttestation Attestation) Make()
    {
        var server = new FakeServer();
        var attestation = new FakeAttestation();
        var service = new AppSessionService(attestation, server.Client(), () => "install-0000000001", new StepClock());
        var client = new HttpClient(new AppSessionHandler(service) { InnerHandler = server }) { BaseAddress = new Uri("https://map.test/") };
        return (client, server, attestation);
    }

    private static HttpResponseMessage SessionRequired() =>
        FakeServer.Problem(HttpStatusCode.Unauthorized, "APP_SESSION_REQUIRED", "App verification required");

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    [Fact]
    public async Task Every_call_carries_the_session()
    {
        var (client, server, _) = Make();

        var response = await client.GetAsync("stations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("session-1", server.Requests.Single(r => r.Path == "/stations").Session);
    }

    [Fact]
    public async Task One_attestation_serves_many_calls()
    {
        var (client, server, attestation) = Make();

        for (var i = 0; i < 5; i++)
            await client.GetAsync("stations");

        Assert.Equal(1, attestation.Calls);
        Assert.Equal(5, server.Count("/stations"));
    }

    [Fact]
    public async Task A_401_session_required_attests_again_once_and_replays_with_the_new_session_and_the_same_body()
    {
        var (client, server, attestation) = Make();
        server.Api = (_, session) => session == "session-1" ? SessionRequired() : FakeServer.Json(HttpStatusCode.OK, "[]");

        using var body = new StringContent("{\"from\":\"a\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("plan", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attestation.Calls);
        var plans = server.Requests.Where(r => r.Path == "/plan").ToList();
        Assert.Equal(2, plans.Count);
        Assert.Equal("session-1", plans[0].Session);
        Assert.Equal("session-2", plans[1].Session);
        Assert.Equal("{\"from\":\"a\"}", plans[1].Body);
    }

    [Fact]
    public async Task A_replay_that_is_refused_too_is_returned_and_not_retried_again()
    {
        var (client, server, attestation) = Make();
        server.Api = (_, _) => SessionRequired();

        var response = await client.GetAsync("stations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("APP_SESSION_REQUIRED", await CodeAsync(response));
        Assert.Equal(2, attestation.Calls);
        Assert.Equal(2, server.Count("/stations"));
    }

    [Fact]
    public async Task A_401_that_is_not_about_the_session_is_left_alone()
    {
        var (client, server, attestation) = Make();
        server.Api = (_, _) => FakeServer.Problem(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", "no");

        var response = await client.GetAsync("stations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await CodeAsync(response));
        Assert.Equal(1, attestation.Calls);
        Assert.Equal(1, server.Count("/stations"));
    }

    [Fact]
    public async Task When_attesting_again_is_refused_the_caller_gets_a_verification_error_and_no_replay()
    {
        var (client, server, _) = Make();
        server.Api = (_, _) => SessionRequired();
        await client.GetAsync("stations");

        // The next attestation (after the 401) is refused by Google Play.
        server.SessionStatus = HttpStatusCode.Forbidden;
        server.SessionCode = "APP_ATTESTATION_FAILED";
        server.Requests.Clear();

        var response = await client.GetAsync("stations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("APP_ATTESTATION_FAILED", await CodeAsync(response));
        Assert.Contains("verify this app", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // One call went out (with the old session); none was replayed.
        Assert.Equal(1, server.Count("/stations"));
    }

    [Fact]
    public async Task When_google_cannot_be_asked_the_error_says_unavailable()
    {
        var (client, server, _) = Make();
        server.Api = (_, _) => SessionRequired();
        await client.GetAsync("stations");

        server.SessionStatus = HttpStatusCode.ServiceUnavailable;
        server.SessionCode = "APP_ATTESTATION_UNAVAILABLE";

        var response = await client.GetAsync("stations");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("APP_ATTESTATION_UNAVAILABLE", await CodeAsync(response));
    }

    [Fact]
    public async Task When_the_server_cannot_be_reached_while_attesting_it_is_a_request_exception_like_any_dead_network()
    {
        var (client, server, _) = Make();
        server.Api = (_, session) =>
        {
            // The session endpoint then goes dead, as if the network dropped between the 401 and the retry.
            server.Unreachable = session is not null;
            return SessionRequired();
        };

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("stations"));
    }

    [Fact]
    public async Task With_no_session_to_send_the_call_still_goes_and_the_server_decides()
    {
        var (client, server, _) = Make();
        server.SessionStatus = HttpStatusCode.Forbidden;
        server.Api = (_, session) => session is null ? FakeServer.Json(HttpStatusCode.OK, "[\"log-only server let it through\"]") : SessionRequired();

        var response = await client.GetAsync("stations");

        // Log-only: the server served a call that had no session. The app did not refuse it on its own.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(server.Requests.Single(r => r.Path == "/stations").Session);
    }

    [Fact]
    public async Task With_no_session_and_an_enforcing_server_the_refusal_is_shown_after_one_more_try()
    {
        var (client, server, _) = Make();
        server.SessionStatus = HttpStatusCode.Forbidden;
        server.Api = (_, _) => SessionRequired();

        var response = await client.GetAsync("stations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("APP_ATTESTATION_FAILED", await CodeAsync(response));
    }

    [Fact]
    public async Task A_one_shot_body_that_cannot_be_replayed_returns_the_401_instead_of_uploading_the_wrong_bytes()
    {
        var (client, server, attestation) = Make();
        server.Api = (_, _) => SessionRequired();
        server.ReadsBodies = false;

        using var body = new OneShotContent("payload");
        var response = await client.PostAsync("data-reports", body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, attestation.Calls);
    }

    private sealed class OneShotContent(string text) : HttpContent
    {
        private bool _used;

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            if (_used)
                throw new InvalidOperationException("one-shot");
            _used = true;
            await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
