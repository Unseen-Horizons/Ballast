// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates.Tests;

/// <summary>
/// Answers each request with the next queued response, and records every request that reaches it. It throws instead when
/// <see cref="ThrowOnSend"/> is set, so no test reaches a real server.
/// </summary>
public sealed class FakeHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);

    /// <summary>Every request that reached the handler, in order.</summary>
    public List<Uri> Urls { get; } = [];

    /// <summary>When set, the handler throws this instead of answering.</summary>
    public Exception? ThrowOnSend { get; init; }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Urls.Add(request.RequestUri!);
        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        return Task.FromResult(_responses.Dequeue());
    }

    /// <summary>A response with a JSON body.</summary>
    /// <param name="status">The status.</param>
    /// <param name="json">The body, as JSON text.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Json(System.Net.HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
