// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Ballast.Testing.Fakes;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that hands out one client over a test's handler, for code that asks the factory for a named
/// client. The handler is not disposed with the client, so one handler can serve several calls.
/// </summary>
public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    /// <summary>A new client over the handler. The name is ignored: every name gets the same handler.</summary>
    /// <param name="name">The client name the code asked for.</param>
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>Response builders for the HTTP fakes. Each returns a new response, so a test can use it once.</summary>
public static class HttpAnswers
{
    /// <summary>A plain-text answer with the given status.</summary>
    /// <param name="status">The status code.</param>
    /// <param name="body">The body, sent as UTF-8.</param>
    public static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    /// <summary>A 200 answer with a binary body.</summary>
    /// <param name="body">The bytes to send.</param>
    public static HttpResponseMessage Ok(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>A 200 answer whose body is streamed from the text, as UTF-8.</summary>
    /// <param name="text">The body text.</param>
    public static HttpResponseMessage Streamed(string text) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text))) };

    /// <summary>A redirect to <paramref name="location"/>, which may be relative.</summary>
    /// <param name="location">The Location header value.</param>
    /// <param name="status">The redirect status. Default 301.</param>
    public static HttpResponseMessage RedirectTo(string location, HttpStatusCode status = HttpStatusCode.MovedPermanently)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>An answer with the given status, a small JSON body, and an optional Retry-After header.</summary>
    /// <param name="status">The status code.</param>
    /// <param name="retryAfter">The Retry-After header, or null for none.</param>
    public static HttpResponseMessage WithStatus(HttpStatusCode status, RetryConditionHeaderValue? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        response.Headers.RetryAfter = retryAfter;
        return response;
    }
}
