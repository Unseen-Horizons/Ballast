// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>
/// Sends each kind of e-mail to each subject at most once, by recording it in an <see cref="ISendOnceStore"/> around the send.
/// The flow is <i>check, compose, reserve, send, mark</i>: the record is reserved before the send, so a crash between the two
/// means a mail that is not sent rather than one that is sent twice. The mail is composed before anything is recorded, so a
/// composition that fails leaves no record behind.
/// </summary>
/// <remarks>
/// <para>
/// The ledger holds no state of its own, so one instance serves every caller. Two callers for the same subject and kind are
/// safe: the store lets only one of them reserve or claim the record, and the other reports <see cref="SendOnceOutcome.InProgress"/>.
/// </para>
/// <para>
/// A send that is cancelled, or whose mark cannot be written, leaves its record Pending. That is the same state a crash leaves,
/// and <see cref="SendOnceOptions.PendingAfter"/> is the only thing that moves it on.
/// </para>
/// </remarks>
public sealed class SendOnceLedger
{
    private readonly ISendOnceStore _store;
    private readonly EmailService _email;
    private readonly TimeProvider _time;
    private readonly bool _retryFailed;
    private readonly TimeSpan _failedRetryWindow;
    private readonly TimeSpan? _pendingAfter;

    /// <summary>Creates the ledger.</summary>
    /// <param name="store">The app's store.</param>
    /// <param name="email">The service that sends the mail.</param>
    /// <param name="options">The rules, or null for the defaults. They are checked here (see <see cref="SendOnceOptions.Validate"/>).</param>
    /// <param name="time">The clock for the retry window and the stale check. Null for the system clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="email"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are inconsistent. See <see cref="SendOnceOptions.Validate"/>.</exception>
    public SendOnceLedger(ISendOnceStore store, EmailService email, SendOnceOptions? options = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(email);
        var rules = options ?? new SendOnceOptions();
        rules.Validate();
        _store = store;
        _email = email;
        _time = time ?? TimeProvider.System;
        _retryFailed = rules.RetryFailed;
        _failedRetryWindow = rules.FailedRetryWindow;
        _pendingAfter = rules.PendingAfter;
    }

    /// <summary>
    /// Sends the e-mail of one kind to one subject, unless a record says it was already sent, is final, or is in progress. When the
    /// subject has no address, the record is written as <see cref="SendOnceState.NoAddress"/> and nothing is sent.
    /// </summary>
    /// <param name="subjectKey">The subject the e-mail is about, as the app names it. Required.</param>
    /// <param name="kind">The kind of e-mail, as the app names it. Required.</param>
    /// <param name="address">The recipient's address, or null or blank when the subject has none. It is trimmed.</param>
    /// <param name="compose">
    /// Builds the e-mail. Called only when the e-mail is going to be sent, before anything is recorded. Its exception propagates,
    /// and the record is left as it was.
    /// </param>
    /// <param name="ct">Cancels the call. A cancellation during the send leaves the record Pending.</param>
    /// <returns>What this call did, and the provider's answer when it sent.</returns>
    /// <exception cref="ArgumentException"><paramref name="subjectKey"/> or <paramref name="kind"/> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="compose"/> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    /// <remarks>
    /// The store's exceptions propagate: a store that cannot read or write the record is not a send outcome. A failure to send
    /// is not an exception: <see cref="EmailService.SendAsync(string, string, string, string?, CancellationToken)"/> reports it,
    /// and the record becomes Failed.
    /// </remarks>
    public async Task<SendOnceResult> SendOnceAsync(string subjectKey, string kind, string? address, Func<CancellationToken, Task<EmailContent>> compose, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(compose);

        var recipient = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        var now = _time.GetUtcNow();
        var recorded = await _store.FindAsync(subjectKey, kind, ct);

        if (recorded is null)
        {
            if (recipient is null)
            {
                var noAddress = await _store.TryReserveAsync(new SendOnceRecord(subjectKey, kind, SendOnceState.NoAddress, null, now), ct);
                return new SendOnceResult(noAddress ? SendOnceOutcome.NoAddress : SendOnceOutcome.InProgress);
            }

            var content = await compose(ct);
            var reserved = await _store.TryReserveAsync(new SendOnceRecord(subjectKey, kind, SendOnceState.Pending, recipient, now), ct);
            if (!reserved)
            {
                return new SendOnceResult(SendOnceOutcome.InProgress);
            }

            return await SendAndMarkAsync(subjectKey, kind, recipient, content, ct);
        }

        if (recorded.State is SendOnceState.Sent or SendOnceState.NoAddress)
        {
            return new SendOnceResult(SendOnceOutcome.AlreadyRecorded);
        }

        if (recipient is null || !MayRetry(recorded, now))
        {
            return new SendOnceResult(recorded.State is SendOnceState.Failed ? SendOnceOutcome.AlreadyFailed : SendOnceOutcome.InProgress);
        }

        var retryContent = await compose(ct);

        // A Failed claim keeps the record's first time, so the retry window keeps counting from the first reservation. A stale
        // Pending claim is a new attempt, so it takes the current time, which lets the next stale check measure from this claim.
        var claimedAt = recorded.State is SendOnceState.Pending ? now : recorded.CreatedAtUtc;
        var claim = new SendOnceClaim(subjectKey, kind, recorded.State, recorded.CreatedAtUtc, claimedAt, recipient);
        if (!await _store.TryClaimAsync(claim, ct))
        {
            return new SendOnceResult(SendOnceOutcome.InProgress);
        }

        return await SendAndMarkAsync(subjectKey, kind, recipient, retryContent, ct);
    }

    /// <summary>
    /// Whether an existing record may be claimed again. A Failed record may be retried while it is no older than the window. A Pending
    /// record may be reclaimed once it is older than <see cref="SendOnceOptions.PendingAfter"/>.
    /// </summary>
    private bool MayRetry(SendOnceRecord recorded, DateTimeOffset now) =>
        recorded.State switch
        {
            SendOnceState.Failed => _retryFailed && now - recorded.CreatedAtUtc <= _failedRetryWindow,
            SendOnceState.Pending => _pendingAfter is { } pendingAfter && now - recorded.CreatedAtUtc > pendingAfter,
            _ => false,
        };

    private async Task<SendOnceResult> SendAndMarkAsync(string subjectKey, string kind, string recipient, EmailContent content, CancellationToken ct)
    {
        var delivery = await _email.SendAsync(recipient, content.Subject, content.Text, content.Html, ct);
        var state = delivery.IsDelivered() ? SendOnceState.Sent : SendOnceState.Failed;
        await _store.MarkAsync(subjectKey, kind, state, ct);
        return new SendOnceResult(state is SendOnceState.Sent ? SendOnceOutcome.Sent : SendOnceOutcome.Failed, delivery);
    }
}
