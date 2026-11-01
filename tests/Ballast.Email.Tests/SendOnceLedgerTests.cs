// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.Email.Tests;

/// <summary>
/// The ledger's flow, its decisions for each stored record, and its races, with an in-memory store and a fake transport. The clock
/// is a fake, so the retry window and the stale check are tested by moving time, not by waiting.
/// </summary>
public class SendOnceLedgerTests
{
    private const string Subject = "42";
    private const string Kind = "reminder-7";
    private const string Address = "klijent@example.test";
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static Func<CancellationToken, Task<EmailContent>> Compose(string text = "Your invoice is ready.") =>
        _ => Task.FromResult(new EmailContent("Your invoice is ready", text));

    private static SendOnceLedger Ledger(
        ISendOnceStore store,
        FakeMailTransport transport,
        FakeTimeProvider time,
        SendOnceOptions? options = null)
    {
        var settings = new MailSettings("SG.not-a-real-key", "info@example.test", "Example");
        var email = new EmailService(settings, NullLogger<EmailService>.Instance, transport);
        return new SendOnceLedger(store, email, options, time);
    }

    private static (SendOnceLedger Ledger, InMemorySendOnceStore Store, FakeMailTransport Transport, FakeTimeProvider Time) Fixture(
        SendOnceOptions? options = null,
        params EmailSendResult[] results)
    {
        var store = new InMemorySendOnceStore();
        var transport = new FakeMailTransport(results);
        var time = new FakeTimeProvider(Start);
        return (Ledger(store, transport, time, options), store, transport, time);
    }

    [Fact]
    public async Task FirstCall_SendsOnce_AndRecordsSent()
    {
        var (ledger, store, transport, _) = Fixture();

        var first = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());
        var second = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(new SendOnceResult(SendOnceOutcome.Sent, EmailSendResult.Sent), first);
        Assert.Equal(SendOnceOutcome.AlreadyRecorded, second.Outcome);
        Assert.Single(transport.Sent);
        Assert.Equal(new SendOnceRecord(Subject, Kind, SendOnceState.Sent, Address, Start), await store.FindAsync(Subject, Kind));
    }

    [Fact]
    public async Task TheAddress_IsTrimmed_BeforeItIsSentAndRecorded()
    {
        var (ledger, store, transport, _) = Fixture();

        await ledger.SendOnceAsync(Subject, Kind, "  klijent@example.test ", Compose());

        Assert.Equal(Address, Assert.Single(transport.Sent).To);
        Assert.Equal(Address, (await store.FindAsync(Subject, Kind))!.Recipient);
    }

    [Fact]
    public async Task TheHtmlPart_OfTheComposedMail_IsSent()
    {
        var (ledger, _, transport, _) = Fixture();

        await ledger.SendOnceAsync(Subject, Kind, Address, _ => Task.FromResult(new EmailContent("Subject", "Text", "<p>Html</p>")));

        Assert.Equal("<p>Html</p>", Assert.Single(transport.Sent).Html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoAddress_RecordsNoAddress_AndSendsAndComposesNothing(string? address)
    {
        var (ledger, store, transport, _) = Fixture();
        var composed = false;

        var first = await ledger.SendOnceAsync(Subject, Kind, address, _ =>
        {
            composed = true;
            return Task.FromResult(new EmailContent("Subject", "Text"));
        });
        var second = await ledger.SendOnceAsync(Subject, Kind, address, Compose());

        Assert.Equal(new SendOnceResult(SendOnceOutcome.NoAddress), first);
        Assert.Equal(SendOnceOutcome.AlreadyRecorded, second.Outcome);
        Assert.False(composed);
        Assert.Empty(transport.Sent);
        Assert.Equal(new SendOnceRecord(Subject, Kind, SendOnceState.NoAddress, null, Start), await store.FindAsync(Subject, Kind));
    }

    [Fact]
    public async Task ComposeFailure_LeavesNoRecord_AndTheNextCallSends()
    {
        var (ledger, store, transport, _) = Fixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ledger.SendOnceAsync(Subject, Kind, Address, _ => throw new InvalidOperationException("The template is missing a value.")));

        Assert.Null(await store.FindAsync(Subject, Kind));
        Assert.Empty(transport.Sent);
        Assert.Equal(SendOnceOutcome.Sent, (await ledger.SendOnceAsync(Subject, Kind, Address, Compose())).Outcome);
    }

    [Fact]
    public async Task LoggedDelivery_CountsAsSent()
    {
        var store = new InMemorySendOnceStore();
        var transport = new FakeMailTransport();
        var email = new EmailService(new MailSettings(null, "info@example.test"), NullLogger<EmailService>.Instance, transport, isDevelopment: true);
        var ledger = new SendOnceLedger(store, email, time: new FakeTimeProvider(Start));

        var result = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(new SendOnceResult(SendOnceOutcome.Sent, EmailSendResult.Logged), result);
        Assert.Equal(SendOnceState.Sent, (await store.FindAsync(Subject, Kind))!.State);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task NotConfigured_RecordsFailed_AsItDoesNotDeliver()
    {
        var store = new InMemorySendOnceStore();
        var email = new EmailService(new MailSettings(null, "info@example.test"), NullLogger<EmailService>.Instance, isDevelopment: false);
        var ledger = new SendOnceLedger(store, email, time: new FakeTimeProvider(Start));

        var result = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(new SendOnceResult(SendOnceOutcome.Failed, EmailSendResult.NotConfigured), result);
        Assert.Equal(SendOnceState.Failed, (await store.FindAsync(Subject, Kind))!.State);
    }

    [Fact]
    public async Task FailedSend_IsRetried_WithinTheWindow()
    {
        var (ledger, store, transport, time) = Fixture(null, EmailSendResult.Failed, EmailSendResult.Sent);

        var first = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());
        time.Advance(TimeSpan.FromHours(47));
        var second = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.Failed, first.Outcome);
        Assert.Equal(SendOnceOutcome.Sent, second.Outcome);
        Assert.Equal(2, transport.Sent.Count);
        Assert.Equal(SendOnceState.Sent, (await store.FindAsync(Subject, Kind))!.State);
    }

    [Fact]
    public async Task FailedSend_IsNotRetried_WhenRetryFailedIsOff()
    {
        var (ledger, store, transport, _) = Fixture(new SendOnceOptions { RetryFailed = false }, EmailSendResult.Failed, EmailSendResult.Sent);

        await ledger.SendOnceAsync(Subject, Kind, Address, Compose());
        var second = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.AlreadyFailed, second.Outcome);
        Assert.Single(transport.Sent);
        Assert.Equal(SendOnceState.Failed, (await store.FindAsync(Subject, Kind))!.State);
    }

    [Fact]
    public async Task FailedSend_IsFinal_OnceOlderThanTheWindow()
    {
        var (ledger, store, transport, time) = Fixture(null, EmailSendResult.Failed, EmailSendResult.Sent);
        await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        time.Advance(TimeSpan.FromHours(49));
        var second = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.AlreadyFailed, second.Outcome);
        Assert.Single(transport.Sent);
        Assert.Equal(SendOnceState.Failed, (await store.FindAsync(Subject, Kind))!.State);
    }

    [Fact]
    public async Task FailedSend_KeepsItsFirstTime_SoTheWindowEndsAfterItsRetries()
    {
        // Two retries inside the window: the window still counts from the first reservation, so the third call is final.
        var (ledger, store, transport, time) = Fixture(null, EmailSendResult.Failed, EmailSendResult.Failed, EmailSendResult.Sent);

        await ledger.SendOnceAsync(Subject, Kind, Address, Compose());
        time.Advance(TimeSpan.FromHours(24));
        var retried = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());
        Assert.Equal(SendOnceOutcome.Failed, retried.Outcome);
        Assert.Equal(Start, (await store.FindAsync(Subject, Kind))!.CreatedAtUtc);

        time.Advance(TimeSpan.FromHours(25));
        var final = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.AlreadyFailed, final.Outcome);
        Assert.Equal(2, transport.Sent.Count);
    }

    [Fact]
    public async Task FailedRecord_WithNoAddress_IsNotRetried()
    {
        var (ledger, _, transport, _) = Fixture(null, EmailSendResult.Failed);
        await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        var second = await ledger.SendOnceAsync(Subject, Kind, null, Compose());

        Assert.Equal(SendOnceOutcome.AlreadyFailed, second.Outcome);
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task StalePending_IsBlocked_WhenPendingAfterIsOff()
    {
        var (ledger, store, transport, time) = Fixture();
        await store.TryReserveAsync(new SendOnceRecord(Subject, Kind, SendOnceState.Pending, Address, Start));

        time.Advance(TimeSpan.FromDays(1000));
        var result = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.InProgress, result.Outcome);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task StalePending_IsReclaimedAndSent_WhenPendingAfterIsOn()
    {
        var options = new SendOnceOptions { RetryFailed = false, PendingAfter = TimeSpan.FromHours(1) };
        var (ledger, store, transport, time) = Fixture(options);
        await store.TryReserveAsync(new SendOnceRecord(Subject, Kind, SendOnceState.Pending, Address, Start));

        time.Advance(TimeSpan.FromHours(2));
        var result = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.Sent, result.Outcome);
        Assert.Single(transport.Sent);
        Assert.Equal(SendOnceState.Sent, (await store.FindAsync(Subject, Kind))!.State);
    }

    [Fact]
    public async Task PendingNotYetStale_IsInProgress_WhenPendingAfterIsOn()
    {
        var options = new SendOnceOptions { RetryFailed = false, PendingAfter = TimeSpan.FromHours(1) };
        var (ledger, store, transport, time) = Fixture(options);
        await store.TryReserveAsync(new SendOnceRecord(Subject, Kind, SendOnceState.Pending, Address, Start));

        time.Advance(TimeSpan.FromMinutes(30));
        var result = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.InProgress, result.Outcome);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task AReclaimedSend_TakesTheClaimTime_SoASecondCallDuringItIsInProgress()
    {
        var options = new SendOnceOptions { RetryFailed = false, PendingAfter = TimeSpan.FromHours(1) };
        var store = new InMemorySendOnceStore();
        var time = new FakeTimeProvider(Start);
        SendOnceLedger? ledger = null;
        SendOnceOutcome? nested = null;
        var transport = new FakeMailTransport
        {
            OnSend = async () =>
            {
                nested = (await ledger!.SendOnceAsync(Subject, Kind, Address, Compose())).Outcome;
            },
        };
        ledger = Ledger(store, transport, time, options);
        await store.TryReserveAsync(new SendOnceRecord(Subject, Kind, SendOnceState.Pending, Address, Start));
        time.Advance(TimeSpan.FromHours(2));

        var result = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.Sent, result.Outcome);
        Assert.Equal(SendOnceOutcome.InProgress, nested);
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task ARetriedSend_IsNotReclaimed_WhileItRuns_WithTheMinimumPendingAfter()
    {
        // PendingAfter is the window plus one hour, the shortest the options accept with RetryFailed on. The retried record keeps its
        // first time, so 30 hours in, a send that is still running must not look stale.
        var options = new SendOnceOptions { PendingAfter = TimeSpan.FromHours(49) };
        var store = new InMemorySendOnceStore();
        var time = new FakeTimeProvider(Start);
        SendOnceLedger? ledger = null;
        SendOnceOutcome? nested = null;
        var calls = 0;
        var transport = new FakeMailTransport(EmailSendResult.Failed)
        {
            OnSend = async () =>
            {
                calls++;
                if (calls == 2)
                {
                    nested = (await ledger!.SendOnceAsync(Subject, Kind, Address, Compose())).Outcome;
                }
            },
        };
        ledger = Ledger(store, transport, time, options);
        await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        time.Advance(TimeSpan.FromHours(30));
        var retry = await ledger.SendOnceAsync(Subject, Kind, Address, Compose());

        Assert.Equal(SendOnceOutcome.Sent, retry.Outcome);
        Assert.Equal(SendOnceOutcome.InProgress, nested);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RacingSenders_ToReserveTheSameMail_OnlyOneSends()
    {
        var inner = new InMemorySendOnceStore();
        var store = new BarrierSendOnceStore(inner, parties: 2);
        var transport = new FakeMailTransport();
        var ledger = Ledger(store, transport, new FakeTimeProvider(Start));

        var results = await Task.WhenAll(
            ledger.SendOnceAsync(Subject, Kind, Address, Compose()),
            ledger.SendOnceAsync(Subject, Kind, Address, Compose()));

        Assert.Single(transport.Sent);
        Assert.Single(results, r => r.Outcome == SendOnceOutcome.Sent);
        Assert.Single(results, r => r.Outcome == SendOnceOutcome.InProgress);
        Assert.Equal(SendOnceState.Sent, (await inner.FindAsync(Subject, Kind))!.State);
    }

    [Fact]
    public async Task RacingSenders_ToReclaimAFailedMail_OnlyOneSends()
    {
        var inner = new InMemorySendOnceStore();
        var time = new FakeTimeProvider(Start);
        await inner.TryReserveAsync(new SendOnceRecord(Subject, Kind, SendOnceState.Failed, Address, Start));
        var store = new BarrierSendOnceStore(inner, parties: 2);
        var transport = new FakeMailTransport();
        var ledger = Ledger(store, transport, time);

        var results = await Task.WhenAll(
            ledger.SendOnceAsync(Subject, Kind, Address, Compose()),
            ledger.SendOnceAsync(Subject, Kind, Address, Compose()));

        Assert.Single(transport.Sent);
        Assert.Single(results, r => r.Outcome == SendOnceOutcome.Sent);
        Assert.Single(results, r => r.Outcome == SendOnceOutcome.InProgress);
    }

    [Fact]
    public async Task RacingSenders_ToReclaimAStalePending_OnlyOneSends()
    {
        var options = new SendOnceOptions { RetryFailed = false, PendingAfter = TimeSpan.FromHours(1) };
        var inner = new InMemorySendOnceStore();
        var time = new FakeTimeProvider(Start);
        await inner.TryReserveAsync(new SendOnceRecord(Subject, Kind, SendOnceState.Pending, Address, Start));
        time.Advance(TimeSpan.FromHours(2));
        var store = new BarrierSendOnceStore(inner, parties: 2);
        var transport = new FakeMailTransport();
        var ledger = Ledger(store, transport, time, options);

        var results = await Task.WhenAll(
            ledger.SendOnceAsync(Subject, Kind, Address, Compose()),
            ledger.SendOnceAsync(Subject, Kind, Address, Compose()));

        Assert.Single(transport.Sent);
        Assert.Single(results, r => r.Outcome == SendOnceOutcome.Sent);
        Assert.Single(results, r => r.Outcome == SendOnceOutcome.InProgress);
    }

    [Fact]
    public async Task StoreFailure_OnRead_Propagates_AndNothingIsSent()
    {
        var transport = new FakeMailTransport();
        var ledger = Ledger(new ThrowingStore(), transport, new FakeTimeProvider(Start));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.SendOnceAsync(Subject, Kind, Address, Compose()));

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task MarkFailure_Propagates_AndLeavesThePendingRecord()
    {
        var inner = new InMemorySendOnceStore();
        var transport = new FakeMailTransport();
        var ledger = Ledger(new MarkThrowingStore(inner), transport, new FakeTimeProvider(Start));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.SendOnceAsync(Subject, Kind, Address, Compose()));

        Assert.Single(transport.Sent);
        Assert.Equal(SendOnceState.Pending, (await inner.FindAsync(Subject, Kind))!.State);
    }

    [Fact]
    public async Task CancelDuringTheSend_PropagatesAndLeavesThePendingRecord()
    {
        var inner = new InMemorySendOnceStore();
        using var cts = new CancellationTokenSource();
        var transport = new FakeMailTransport { OnSend = () => cts.CancelAsync() };
        var ledger = Ledger(inner, transport, new FakeTimeProvider(Start));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ledger.SendOnceAsync(Subject, Kind, Address, Compose(), cts.Token));

        Assert.Equal(SendOnceState.Pending, (await inner.FindAsync(Subject, Kind))!.State);
    }

    [Fact]
    public void Constructor_RefusesInconsistentOptions()
    {
        var store = new InMemorySendOnceStore();
        var settings = new MailSettings("SG.not-a-real-key", "info@example.test");
        var email = new EmailService(settings, NullLogger<EmailService>.Instance);

        Assert.Throws<ArgumentOutOfRangeException>(() => new SendOnceLedger(store, email, new SendOnceOptions { PendingAfter = TimeSpan.FromHours(10) }));
    }

    [Fact]
    public async Task InvalidArguments_Throw()
    {
        var (ledger, _, _, _) = Fixture();

        await Assert.ThrowsAsync<ArgumentException>(() => ledger.SendOnceAsync(" ", Kind, Address, Compose()));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => ledger.SendOnceAsync(Subject, null!, Address, Compose()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ledger.SendOnceAsync(Subject, Kind, Address, null!));
    }

    /// <summary>A store that cannot be read.</summary>
    private sealed class ThrowingStore : ISendOnceStore
    {
        public Task<SendOnceRecord?> FindAsync(string subjectKey, string kind, CancellationToken ct = default) =>
            Task.FromException<SendOnceRecord?>(new InvalidOperationException("The store is down."));

        public Task<bool> TryReserveAsync(SendOnceRecord record, CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> TryClaimAsync(SendOnceClaim claim, CancellationToken ct = default) => Task.FromResult(true);

        public Task MarkAsync(string subjectKey, string kind, SendOnceState state, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>A store whose mark fails, after the send.</summary>
    private sealed class MarkThrowingStore(ISendOnceStore inner) : ISendOnceStore
    {
        public Task<SendOnceRecord?> FindAsync(string subjectKey, string kind, CancellationToken ct = default) => inner.FindAsync(subjectKey, kind, ct);

        public Task<bool> TryReserveAsync(SendOnceRecord record, CancellationToken ct = default) => inner.TryReserveAsync(record, ct);

        public Task<bool> TryClaimAsync(SendOnceClaim claim, CancellationToken ct = default) => inner.TryClaimAsync(claim, ct);

        public Task MarkAsync(string subjectKey, string kind, SendOnceState state, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("The database closed the connection."));
    }
}
