// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AppAttestation.Tests.Support;

namespace Ballast.AppAttestation.Tests;

/// <summary>
/// The default stores: single use, expiry on the clock they are given, and a full store that refuses rather than evicting a live entry.
/// </summary>
public class StoreTests
{
    private static readonly WebTicketGrant Grant = new("session-token", new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_nonce_answers_once()
    {
        var store = new InMemoryAppSessionNonceStore(new FakeClock(), 10);

        Assert.True(await store.AddAsync("n1", TimeSpan.FromMinutes(5)));

        Assert.True(await store.TakeAsync("n1"));
        Assert.False(await store.TakeAsync("n1"));
    }

    [Fact]
    public async Task A_nonce_that_was_never_added_does_not_answer()
    {
        var store = new InMemoryAppSessionNonceStore(new FakeClock(), 10);

        Assert.False(await store.TakeAsync("never"));
    }

    [Fact]
    public async Task A_nonce_does_not_answer_after_its_lifetime()
    {
        var clock = new FakeClock();
        var store = new InMemoryAppSessionNonceStore(clock, 10);
        await store.AddAsync("n1", TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(await store.TakeAsync("n1"));
    }

    [Fact]
    public async Task A_full_nonce_store_refuses_the_next_nonce_and_keeps_the_live_ones()
    {
        var clock = new FakeClock();
        var store = new InMemoryAppSessionNonceStore(clock, 2);
        Assert.True(await store.AddAsync("a", TimeSpan.FromMinutes(5)));
        Assert.True(await store.AddAsync("b", TimeSpan.FromMinutes(5)));

        Assert.False(await store.AddAsync("c", TimeSpan.FromMinutes(5)));

        Assert.True(await store.TakeAsync("a"));
        Assert.True(await store.TakeAsync("b"));
    }

    [Fact]
    public async Task A_full_nonce_store_makes_room_only_from_expired_entries()
    {
        var clock = new FakeClock();
        var store = new InMemoryAppSessionNonceStore(clock, 1);
        await store.AddAsync("old", TimeSpan.FromMinutes(1));

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(await store.AddAsync("new", TimeSpan.FromMinutes(5)));
        Assert.False(await store.TakeAsync("old"));
        Assert.True(await store.TakeAsync("new"));
    }

    [Fact]
    public async Task A_ticket_gives_back_its_grant_once()
    {
        var store = new InMemoryAppSessionTicketStore(new FakeClock(), 10);
        Assert.True(await store.AddAsync("t1", Grant, TimeSpan.FromSeconds(60)));

        Assert.Equal(Grant, await store.TakeAsync("t1"));
        Assert.Null(await store.TakeAsync("t1"));
    }

    [Fact]
    public async Task A_ticket_does_not_answer_after_sixty_seconds()
    {
        var clock = new FakeClock();
        var store = new InMemoryAppSessionTicketStore(clock, 10);
        await store.AddAsync("t1", Grant, TimeSpan.FromSeconds(60));

        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Null(await store.TakeAsync("t1"));
    }

    [Fact]
    public async Task A_full_ticket_store_refuses_the_next_ticket()
    {
        var store = new InMemoryAppSessionTicketStore(new FakeClock(), 1);
        Assert.True(await store.AddAsync("t1", Grant, TimeSpan.FromSeconds(60)));

        Assert.False(await store.AddAsync("t2", Grant, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void A_store_with_no_room_at_all_is_refused_at_construction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryAppSessionNonceStore(new FakeClock(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryAppSessionTicketStore(new FakeClock(), 0));
    }

    [Fact]
    public void Both_default_stores_are_behind_an_interface_an_app_can_replace()
    {
        IAppSessionNonceStore nonces = new InMemoryAppSessionNonceStore(new FakeClock(), 1);
        IAppSessionTicketStore tickets = new InMemoryAppSessionTicketStore(new FakeClock(), 1);

        Assert.NotNull(nonces);
        Assert.NotNull(tickets);
    }
}
