// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Ballast.Http.Tests;

/// <summary>An <see cref="IHttpClientFactory"/> that hands out one client over a test's handler, without disposing it.</summary>
internal sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>A clock the test moves: a wait advances it, so a gap is measured on it, and nothing sleeps.</summary>
internal sealed class Clock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>What one request looked like when it reached the handler.</summary>
internal sealed record SeenRequest(Uri Url, HttpMethod Method, string? Authorization, string? Body);

/// <summary>
/// Answers each request from a script, records what was asked, and turns a thrown failure into a failed send. Requests are
/// recorded before the script runs, so a refused hop that was sent shows up in <see cref="Requests"/>.
/// </summary>
internal sealed class ScriptedWeb(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<SeenRequest> Requests { get; } = [];

    public List<Uri> Requested => Requests.Select(r => r.Url).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new SeenRequest(
            request.RequestUri!,
            request.Method,
            request.Headers.Authorization?.ToString(),
            body));
        return respond(request);
    }
}

/// <summary>Response builders shared by the tests.</summary>
internal static class Answer
{
    public static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    public static HttpResponseMessage Ok(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    public static HttpResponseMessage Streamed(string text) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text))) };

    public static HttpResponseMessage RedirectTo(string location, HttpStatusCode status = HttpStatusCode.MovedPermanently)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    public static HttpResponseMessage WithStatus(HttpStatusCode status, RetryConditionHeaderValue? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        response.Headers.RetryAfter = retryAfter;
        return response;
    }
}
