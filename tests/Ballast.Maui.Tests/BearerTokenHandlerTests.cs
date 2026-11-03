// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;
using System.Text.Json;

using Ballast.Maui.Auth;

namespace Ballast.Maui.Tests;

/// <summary>
/// The bearer handler: a token within five minutes of its end is renewed before it is sent, and a 401 is answered by one refresh and
/// one replay. A refused refresh signs the rider out; an unreachable server keeps the session and returns the 401. Behavioural form of
/// the source scan that pinned the 401 path to the sign-out in the app.
/// </summary>
public class BearerTokenHandlerTests
{
    private static readonly Uri Base = new("https://api.test/");

    private static string Encode(string text) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A JWT with the given end, the only claim the handler reads.</summary>
    private static string Jwt(DateTimeOffset expires) =>
        $"{Encode("{\"alg\":\"none\"}")}.{Encode(JsonSerializer.Serialize(new { exp = expires.ToUnixTimeSeconds() }))}.signature";

    private static HttpResponseMessage Unauthorized() => new(HttpStatusCode.Unauthorized);

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };

    private static HttpClient Client(ScriptedApi api, TokenSession session, StepClock clock) =>
        new(new BearerTokenHandler(session, clock) { InnerHandler = api }) { BaseAddress = Base };

    [Fact]
    public async Task A_token_with_time_left_is_sent_as_it_is_and_not_renewed()
    {
        var clock = new StepClock();
        var store = new MemoryTokenStore();
        store.Seed(new TokenPair(Jwt(clock.GetUtcNow().AddHours(1)), "r1"));
        var call = new ScriptedRefreshCall();
        var api = new ScriptedApi(_ => Ok());
        using var session = new TokenSession(store, call);

        var response = await Client(api, session, clock).GetAsync("stations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(store.Pair!.AccessToken, api.Authorizations.Single());
        Assert.Empty(call.Sent);
    }

    [Fact]
    public async Task A_token_within_five_minutes_of_its_end_is_renewed_before_it_is_sent()
    {
        var clock = new StepClock();
        var store = new MemoryTokenStore();
        store.Seed(new TokenPair(Jwt(clock.GetUtcNow().AddMinutes(4)), "r1"));
        var fresh = Jwt(clock.GetUtcNow().AddHours(1));
        var call = new ScriptedRefreshCall { Answer = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Refreshed, new TokenPair(fresh, "r2"))) };
        var api = new ScriptedApi(_ => Ok());
        using var session = new TokenSession(store, call);

        await Client(api, session, clock).GetAsync("stations");

        Assert.Equal("r1", Assert.Single(call.Sent));
        Assert.Equal(fresh, api.Authorizations.Single());
    }

    [Fact]
    public async Task A_token_that_is_not_a_jwt_is_sent_without_a_renewal()
    {
        var clock = new StepClock();
        var store = new MemoryTokenStore();
        store.Seed(new TokenPair("opaque-token", "r1"));
        var call = new ScriptedRefreshCall();
        var api = new ScriptedApi(_ => Ok());
        using var session = new TokenSession(store, call);

        await Client(api, session, clock).GetAsync("stations");

        Assert.Equal("opaque-token", api.Authorizations.Single());
        Assert.Empty(call.Sent);
    }

    [Fact]
    public async Task The_401_path_signs_out_only_on_a_refused_refresh_and_returns_the_401()
    {
        var clock = new StepClock();
        var store = new MemoryTokenStore();
        store.Seed(new TokenPair(Jwt(clock.GetUtcNow().AddHours(1)), "r1"));
        var hooks = new RecordingSessionHooks(store);
        var call = new ScriptedRefreshCall { Answer = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Rejected)) };
        var api = new ScriptedApi(_ => Unauthorized());
        using var session = new TokenSession(store, call, hooks);

        var response = await Client(api, session, clock).GetAsync("stations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("signed-out", Assert.Single(hooks.Events));
        Assert.Single(api.Authorizations);
    }

    [Fact]
    public async Task A_401_with_an_unreachable_refresh_keeps_the_session_and_returns_the_401()
    {
        var clock = new StepClock();
        var store = new MemoryTokenStore();
        var seeded = new TokenPair(Jwt(clock.GetUtcNow().AddHours(1)), "r1");
        store.Seed(seeded);
        var hooks = new RecordingSessionHooks(store);
        var call = new ScriptedRefreshCall { Answer = _ => Task.FromException<TokenRefreshReply>(new HttpRequestException("down")) };
        var api = new ScriptedApi(_ => Unauthorized());
        using var session = new TokenSession(store, call, hooks);

        var response = await Client(api, session, clock).GetAsync("stations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(hooks.Events);
        Assert.Equal(seeded, store.Pair);
        Assert.Single(api.Authorizations);
    }

    [Fact]
    public async Task A_401_that_a_refresh_repairs_is_replayed_once_with_the_new_token()
    {
        var clock = new StepClock();
        var store = new MemoryTokenStore();
        var old = Jwt(clock.GetUtcNow().AddHours(1));
        store.Seed(new TokenPair(old, "r1"));
        var call = new ScriptedRefreshCall
        {
            Answer = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Refreshed, new TokenPair("new-access", "r2"))),
        };
        var api = new ScriptedApi(request => request.Headers.Authorization?.Parameter == old ? Unauthorized() : Ok());
        using var session = new TokenSession(store, call);

        var response = await Client(api, session, clock).GetAsync("stations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { old, "new-access" }, api.Authorizations);
        Assert.Equal("r1", Assert.Single(call.Sent));
        Assert.Equal("new-access", store.Pair!.AccessToken);
    }

    [Fact]
    public async Task A_replay_refused_again_is_returned_and_not_retried()
    {
        var clock = new StepClock();
        var store = new MemoryTokenStore();
        store.Seed(new TokenPair(Jwt(clock.GetUtcNow().AddHours(1)), "r1"));
        var call = new ScriptedRefreshCall
        {
            Answer = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Refreshed, new TokenPair("new-access", "r2"))),
        };
        var api = new ScriptedApi(_ => Unauthorized());
        using var session = new TokenSession(store, call);

        var response = await Client(api, session, clock).GetAsync("stations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, api.Authorizations.Count);
        Assert.Single(call.Sent);
    }

    [Fact]
    public async Task A_post_body_is_replayed_unchanged_after_a_refresh()
    {
        var clock = new StepClock();
        var store = new MemoryTokenStore();
        var old = Jwt(clock.GetUtcNow().AddHours(1));
        store.Seed(new TokenPair(old, "r1"));
        var call = new ScriptedRefreshCall
        {
            Answer = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Refreshed, new TokenPair("new-access", "r2"))),
        };
        var api = new ScriptedApi(request => request.Headers.Authorization?.Parameter == old ? Unauthorized() : Ok());
        using var session = new TokenSession(store, call);
        using var body = new StringContent("{\"from\":\"a\"}", Encoding.UTF8, "application/json");

        var response = await Client(api, session, clock).PostAsync("plan", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, api.Bodies.Count);
        Assert.All(api.Bodies, body => Assert.Equal("{\"from\":\"a\"}", body));
    }
}
