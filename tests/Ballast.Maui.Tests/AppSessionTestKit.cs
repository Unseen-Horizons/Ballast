// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;

using Ballast.Maui.AppAttestation;

namespace Ballast.Maui.Tests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class StepClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Runs once, on the next read of the clock, so a test can act at an exact point inside the service.</summary>
    public Action? OnNextRead { get; set; }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        if (OnNextRead is { } hook)
        {
            OnNextRead = null;
            hook();
        }
        return _now;
    }

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>The platform attestation: returns what the test says, and counts.</summary>
internal sealed class FakeAttestation : IAppAttestation
{
    /// <inheritdoc />
    public string Platform => "android";

    /// <summary>The token to return. Null means the platform cannot attest.</summary>
    public string? Token { get; set; } = "play-token";

    /// <summary>How many times a token was asked for.</summary>
    public int Calls { get; private set; }

    /// <summary>Every nonce a token was asked for, in order.</summary>
    public List<string> Nonces { get; } = [];

    /// <inheritdoc />
    public Task<string?> GetTokenAsync(string nonce, CancellationToken ct = default)
    {
        Calls++;
        Nonces.Add(nonce);
        return Task.FromResult(Token);
    }
}

/// <summary>One recorded request: the verb, the path, the session header it carried and its body.</summary>
internal sealed record Seen(HttpMethod Method, string Path, string? Session, string Body);

/// <summary>
/// The server as a function: the bootstrap routes answer the way the real ones do (a nonce, a session), and every other route is the
/// test's own. Every request is recorded.
/// </summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private int _sessions;

    /// <summary>Every request, in order.</summary>
    public List<Seen> Requests { get; } = [];

    /// <summary>The status <c>POST app/session</c> answers with. 200 issues <c>session-N</c>.</summary>
    public HttpStatusCode SessionStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>The code the refused session answer carries.</summary>
    public string SessionCode { get; set; } = "APP_ATTESTATION_FAILED";

    /// <summary>
    /// Whether the fake reads (and so buffers) request bodies. A real transport streams them and cannot read them twice; a test of that
    /// turns this off.
    /// </summary>
    public bool ReadsBodies { get; set; } = true;

    /// <summary>Make the next requests throw like a dead network.</summary>
    public bool Unreachable { get; set; }

    /// <summary>Seconds a session lasts.</summary>
    public int ExpiresInSeconds { get; set; } = 24 * 3600;

    /// <summary>Runs as <c>POST app/session</c> is about to answer 200 with a session.</summary>
    public Action? OnSessionIssued { get; set; }

    /// <summary>Answers any route that is not the bootstrap. Receives the session header the call carried.</summary>
    public Func<HttpRequestMessage, string?, HttpResponseMessage> Api { get; set; } = (_, _) => new HttpResponseMessage(HttpStatusCode.OK);

    /// <summary>How many requests went to a path.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The count.</returns>
    public int Count(string path) => Requests.Count(r => r.Path == path);

    /// <summary>How many sessions were issued.</summary>
    public int SessionsIssued => _sessions;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = string.Empty;
        if (request.Content is not null)
        {
            if (ReadsBodies)
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            else
                await request.Content.CopyToAsync(Stream.Null, cancellationToken); // streamed to the wire, not buffered
        }
        var session = request.Headers.TryGetValues(AppSessionService.HeaderName, out var values) ? values.FirstOrDefault() : null;
        var path = request.RequestUri!.AbsolutePath;
        lock (Requests)
            Requests.Add(new Seen(request.Method, path, session, body));

        if (Unreachable)
            throw new HttpRequestException("connection refused");

        // Yield so concurrent callers really interleave.
        await Task.Yield();

        if (path == "/app/session/nonce")
            return Json(HttpStatusCode.OK, $"{{\"nonce\":\"nonce-{Requests.Count}\"}}");

        if (path == "/app/session" && request.Method == HttpMethod.Post)
        {
            if (SessionStatus != HttpStatusCode.OK)
                return Problem(SessionStatus, SessionCode, "refused");
            var n = Interlocked.Increment(ref _sessions);
            OnSessionIssued?.Invoke();
            return Json(HttpStatusCode.OK, $"{{\"accessToken\":\"session-{n}\",\"expiresInSeconds\":{ExpiresInSeconds}}}");
        }

        return Api(request, session);
    }

    /// <summary>A JSON answer.</summary>
    /// <param name="status">The status.</param>
    /// <param name="json">The body.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A problem answer, with a code the app reads.</summary>
    /// <param name="status">The status.</param>
    /// <param name="code">The code.</param>
    /// <param name="title">The title.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string title) =>
        new(status) { Content = new StringContent($"{{\"title\":\"{title}\",\"status\":{(int)status},\"code\":\"{code}\"}}", Encoding.UTF8, "application/problem+json") };

    /// <summary>An HTTP client over this server, at the map's address.</summary>
    /// <returns>The client.</returns>
    public HttpClient Client() => new(this) { BaseAddress = new Uri("https://map.test/") };
}
