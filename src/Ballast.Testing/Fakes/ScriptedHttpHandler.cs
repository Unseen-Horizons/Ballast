// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Testing.Fakes;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers each request from a script and records every request that reaches it, so no test
/// leaves the machine. Requests are recorded before the script runs, so a request that was sent shows up in
/// <see cref="Requests"/> even when the script then fails it.
/// </summary>
/// <remarks>
/// Set <see cref="ThrowOnSend"/> to make every send throw instead of answer, and <see cref="CancelOnSend"/> to cancel the caller's
/// token during the send. Both are checked after the request is recorded.
/// </remarks>
public sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    private readonly List<SentRequest> _requests = [];

    /// <summary>Creates a handler whose answers come from <paramref name="respond"/>.</summary>
    /// <param name="respond">Builds the answer for each request. Required.</param>
    public ScriptedHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        _respond = respond;
    }

    /// <summary>When set, every send throws this instead of answering.</summary>
    public Exception? ThrowOnSend { get; set; }

    /// <summary>When set, every send cancels this source, so the caller's token is cancelled during the call.</summary>
    public CancellationTokenSource? CancelOnSend { get; set; }

    /// <summary>Every request that reached the handler, in order. A copy.</summary>
    public IReadOnlyList<SentRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The URL of every request that reached the handler, in order.</summary>
    public IReadOnlyList<Uri> RequestedUrls => Requests.Select(request => request.Url).ToList();

    /// <summary>
    /// A handler that answers the requests with the given responses, one each, in order. A request after the last response
    /// throws <see cref="InvalidOperationException"/>, so a test that sends too many requests fails instead of hanging.
    /// </summary>
    /// <param name="responses">The answers, in the order the requests will arrive.</param>
    public static ScriptedHttpHandler FromResponses(params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        return new ScriptedHttpHandler(_ => queue.Count > 0
            ? queue.Dequeue()
            : throw new InvalidOperationException("The scripted handler has no response left for this request."));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
        {
            _requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                body));
        }

        if (CancelOnSend is not null)
        {
            await CancelOnSend.CancelAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        return _respond(request);
    }

    /// <summary>One request that reached the handler.</summary>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Url">The request URI.</param>
    /// <param name="Authorization">The Authorization header as sent, or null.</param>
    /// <param name="ContentType">The media type of the body, or null when there is no body.</param>
    /// <param name="Body">The body as text, or null when there is no body.</param>
    public sealed record SentRequest(HttpMethod Method, Uri Url, string? Authorization, string? ContentType, string? Body);
}
