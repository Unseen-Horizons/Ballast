// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email.Tests;

/// <summary>
/// Records each send and answers with the queued results, in order. When the queue is empty it answers <see cref="EmailSendResult.Sent"/>.
/// <see cref="OnSend"/> runs during a send, before the answer, so a test can make a second call while the first is in flight.
/// </summary>
public sealed class FakeMailTransport(params EmailSendResult[] results) : IMailTransport
{
    private readonly Queue<EmailSendResult> _results = new(results);

    /// <summary>Every send that reached the transport, in order.</summary>
    public List<SentMail> Sent { get; } = [];

    /// <summary>When set, the transport throws this instead of answering.</summary>
    public Exception? ThrowOnSend { get; init; }

    /// <summary>When set, runs during each send, before the answer.</summary>
    public Func<Task>? OnSend { get; init; }

    /// <inheritdoc />
    public async Task<EmailSendResult> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default)
    {
        Sent.Add(new SentMail(to, subject, text, html));
        if (OnSend is not null)
        {
            await OnSend();
        }

        // A cancelled caller token is observed, as a real transport observes it.
        ct.ThrowIfCancellationRequested();

        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        return _results.Count > 0 ? _results.Dequeue() : EmailSendResult.Sent;
    }

    /// <summary>One send that reached the transport.</summary>
    public sealed record SentMail(string To, string Subject, string Text, string? Html);
}
