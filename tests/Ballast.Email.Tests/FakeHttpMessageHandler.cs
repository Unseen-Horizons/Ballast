// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email.Tests;

/// <summary>
/// Answers each request with the next queued response, and records every request that reaches it. It throws instead when
/// <see cref="ThrowOnSend"/> is set, and cancels the caller's token when <see cref="CancelOnSend"/> is set, so no test reaches
/// SendGrid.
/// </summary>
public sealed class FakeSendGridHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);

    /// <summary>Every request that reached the handler, in order.</summary>
    public List<SentRequest> Requests { get; } = [];

    /// <summary>When set, the handler throws this instead of answering.</summary>
    public Exception? ThrowOnSend { get; init; }

    /// <summary>When set, the handler cancels this source, so the caller's token is cancelled during the call.</summary>
    public CancellationTokenSource? CancelOnSend { get; init; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new SentRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            body));

        if (CancelOnSend is not null)
        {
            await CancelOnSend.CancelAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        return _responses.Dequeue();
    }

    /// <summary>One request that reached the handler.</summary>
    public sealed record SentRequest(HttpMethod Method, Uri Url, string? Authorization, string? ContentType, string Body);
}
