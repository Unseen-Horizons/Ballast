// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Auth;

namespace Ballast.Maui.Tests;

/// <summary>
/// The refresh itself: one refresh in flight at a time, the rotated pair stored, a refused or unreachable server judged as the
/// outcome says, and an unreadable reply ending the session after the drop hook has read the tokens.
/// </summary>
public class TokenSessionTests
{
    private static TokenPair Pair(string access, string refresh) => new(access, refresh);

    [Fact]
    public async Task A_refreshed_session_stores_the_rotated_pair()
    {
        var store = new MemoryTokenStore();
        store.Seed(Pair("old-access", "old-refresh"));
        var call = new ScriptedRefreshCall
        {
            Answer = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Refreshed, Pair("new-access", "new-refresh"))),
        };
        using var session = new TokenSession(store, call);

        var outcome = await session.RefreshAsync();

        Assert.Equal(RefreshOutcome.Refreshed, outcome);
        Assert.Equal(Pair("new-access", "new-refresh"), store.Pair);
        Assert.Equal("old-refresh", Assert.Single(call.Sent));
    }

    [Fact]
    public async Task A_rejected_refresh_leaves_the_stored_tokens_for_the_sign_out_hook_to_clear()
    {
        var store = new MemoryTokenStore();
        store.Seed(Pair("old-access", "old-refresh"));
        var call = new ScriptedRefreshCall { Answer = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Rejected)) };
        using var session = new TokenSession(store, call);

        var outcome = await session.RefreshAsync();

        Assert.Equal(RefreshOutcome.Rejected, outcome);
        Assert.Equal(Pair("old-access", "old-refresh"), store.Pair);
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, store.Clears);
    }

    [Fact]
    public async Task A_refresh_that_cannot_reach_the_server_is_unavailable_and_keeps_the_session()
        => await AssertUnavailableAsync(new HttpRequestException("connection refused"));

    [Fact]
    public async Task A_refresh_that_times_out_is_unavailable_and_keeps_the_session()
        => await AssertUnavailableAsync(new TaskCanceledException("timed out"));

    [Fact]
    public async Task An_unreadable_refresh_reply_drops_the_tokens_after_the_hook_has_read_them()
    {
        var store = new MemoryTokenStore();
        store.Seed(Pair("old-access", "old-refresh"));
        var hooks = new RecordingSessionHooks(store);
        var call = new ScriptedRefreshCall { Answer = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Refreshed)) };
        using var session = new TokenSession(store, call, hooks);

        var outcome = await session.RefreshAsync();

        // The drop hook runs while the tokens are still stored (the owner key is derived from them), then they are cleared.
        Assert.Equal(RefreshOutcome.Rejected, outcome);
        Assert.Equal("dropped:stored", Assert.Single(hooks.Events));
        Assert.Null(store.Pair);
        Assert.Equal(1, store.Clears);
    }

    [Fact]
    public async Task Without_a_refresh_token_the_server_is_not_asked_and_the_outcome_is_rejected()
    {
        var store = new MemoryTokenStore();
        var call = new ScriptedRefreshCall();
        using var session = new TokenSession(store, call);

        var outcome = await session.RefreshAsync();

        Assert.Equal(RefreshOutcome.Rejected, outcome);
        Assert.Empty(call.Sent);
    }

    [Fact]
    public async Task Concurrent_refreshes_share_one_call_to_the_server()
    {
        var store = new MemoryTokenStore();
        store.Seed(Pair("a1", "r1"));
        var call = new ScriptedRefreshCall
        {
            Answer = async _ =>
            {
                // Yield so the other callers really queue behind this refresh.
                await Task.Yield();
                return new TokenRefreshReply(RefreshOutcome.Refreshed, Pair("a2", "r2"));
            },
        };
        using var session = new TokenSession(store, call);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => session.RefreshAsync()));

        Assert.Single(call.Sent);
        Assert.All(outcomes, outcome => Assert.Equal(RefreshOutcome.Refreshed, outcome));
        Assert.Equal(Pair("a2", "r2"), store.Pair);
        Assert.Equal(1, store.Writes);
    }

    private static async Task AssertUnavailableAsync(Exception failure)
    {
        var store = new MemoryTokenStore();
        store.Seed(Pair("old-access", "old-refresh"));
        var call = new ScriptedRefreshCall { Answer = _ => Task.FromException<TokenRefreshReply>(failure) };
        using var session = new TokenSession(store, call);

        var outcome = await session.RefreshAsync();

        Assert.Equal(RefreshOutcome.Unavailable, outcome);
        Assert.Equal(Pair("old-access", "old-refresh"), store.Pair);
        Assert.Equal(0, store.Clears);
    }
}
