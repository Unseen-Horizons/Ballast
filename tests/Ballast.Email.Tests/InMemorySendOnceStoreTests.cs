// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email.Tests;

/// <summary>The contract of <see cref="ISendOnceStore"/>, as the in-memory store keeps it.</summary>
public class InMemorySendOnceStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Find_BeforeAnyReservation_ReturnsNull()
    {
        var store = new InMemorySendOnceStore();

        Assert.Null(await store.FindAsync("42", "reminder-7"));
    }

    [Fact]
    public async Task TryReserve_TheSecondReservationIsRefused_AndTheFirstStays()
    {
        var store = new InMemorySendOnceStore();

        Assert.True(await store.TryReserveAsync(new SendOnceRecord("42", "reminder-7", SendOnceState.Pending, "a@example.test", Start)));
        Assert.False(await store.TryReserveAsync(new SendOnceRecord("42", "reminder-7", SendOnceState.NoAddress, null, Start.AddHours(1))));

        var record = await store.FindAsync("42", "reminder-7");
        Assert.Equal(new SendOnceRecord("42", "reminder-7", SendOnceState.Pending, "a@example.test", Start), record);
    }

    [Fact]
    public async Task TryReserve_OtherKindOfTheSameSubject_IsAllowed()
    {
        var store = new InMemorySendOnceStore();

        Assert.True(await store.TryReserveAsync(new SendOnceRecord("42", "reminder-7", SendOnceState.Pending, "a@example.test", Start)));
        Assert.True(await store.TryReserveAsync(new SendOnceRecord("42", "reminder-21", SendOnceState.Pending, "a@example.test", Start)));
    }

    [Fact]
    public async Task TryClaim_WithTheExpectedStateAndTime_Wins_AndWritesTheClaim()
    {
        var store = new InMemorySendOnceStore();
        await store.TryReserveAsync(new SendOnceRecord("42", "reminder-7", SendOnceState.Failed, "old@example.test", Start));

        var won = await store.TryClaimAsync(new SendOnceClaim("42", "reminder-7", SendOnceState.Failed, Start, Start, "new@example.test"));

        Assert.True(won);
        Assert.Equal(new SendOnceRecord("42", "reminder-7", SendOnceState.Pending, "new@example.test", Start), await store.FindAsync("42", "reminder-7"));
    }

    [Fact]
    public async Task TryClaim_WithAnotherState_Loses()
    {
        var store = new InMemorySendOnceStore();
        await store.TryReserveAsync(new SendOnceRecord("42", "reminder-7", SendOnceState.Sent, "a@example.test", Start));

        Assert.False(await store.TryClaimAsync(new SendOnceClaim("42", "reminder-7", SendOnceState.Failed, Start, Start, "a@example.test")));
        Assert.Equal(SendOnceState.Sent, (await store.FindAsync("42", "reminder-7"))!.State);
    }

    [Fact]
    public async Task TryClaim_WithAnotherTime_Loses()
    {
        var store = new InMemorySendOnceStore();
        await store.TryReserveAsync(new SendOnceRecord("42", "reminder-7", SendOnceState.Pending, "a@example.test", Start));

        Assert.False(await store.TryClaimAsync(new SendOnceClaim("42", "reminder-7", SendOnceState.Pending, Start.AddMinutes(1), Start.AddHours(2), "a@example.test")));
    }

    [Fact]
    public async Task TryClaim_OfAMissingRecord_Loses()
    {
        var store = new InMemorySendOnceStore();

        Assert.False(await store.TryClaimAsync(new SendOnceClaim("42", "reminder-7", SendOnceState.Failed, Start, Start, "a@example.test")));
    }

    [Fact]
    public async Task TryClaim_TwoClaimsOfOneRecord_OnlyOneWins()
    {
        var store = new InMemorySendOnceStore();
        await store.TryReserveAsync(new SendOnceRecord("42", "reminder-7", SendOnceState.Failed, "a@example.test", Start));
        var claim = new SendOnceClaim("42", "reminder-7", SendOnceState.Failed, Start, Start, "a@example.test");

        var results = await Task.WhenAll(store.TryClaimAsync(claim), store.TryClaimAsync(claim));

        Assert.Single(results, won => won);
    }

    [Fact]
    public async Task TryReserve_ManyConcurrentReservations_OnlyOneWins()
    {
        var store = new InMemorySendOnceStore();
        var record = new SendOnceRecord("42", "reminder-7", SendOnceState.Pending, "a@example.test", Start);

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => store.TryReserveAsync(record))));

        Assert.Single(results, won => won);
    }

    [Fact]
    public async Task Mark_SetsTheStateOnly_AndKeepsTheRecipientAndTheTime()
    {
        var store = new InMemorySendOnceStore();
        await store.TryReserveAsync(new SendOnceRecord("42", "reminder-7", SendOnceState.Pending, "a@example.test", Start));

        await store.MarkAsync("42", "reminder-7", SendOnceState.Sent);

        Assert.Equal(new SendOnceRecord("42", "reminder-7", SendOnceState.Sent, "a@example.test", Start), await store.FindAsync("42", "reminder-7"));
    }

    [Fact]
    public async Task Mark_OfAMissingRecord_Throws()
    {
        var store = new InMemorySendOnceStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MarkAsync("42", "reminder-7", SendOnceState.Sent));
    }
}
