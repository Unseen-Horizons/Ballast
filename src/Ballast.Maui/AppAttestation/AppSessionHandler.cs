// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

using Ballast.Errors;
using Ballast.Maui.Auth;

namespace Ballast.Maui.AppAttestation;

/// <summary>
/// Puts the app session on every call to the server (<c>X-App-Session</c>), and answers the server refusing it: on a 401
/// <c>APP_SESSION_REQUIRED</c> the app attests again once and replays the call (<see cref="ReplayAfterRefreshHandler"/>).
/// </summary>
/// <remarks>
/// <para>
/// It never decides on its own that a call may not go out. With no session to send (attestation is failing, or backing off) the
/// call goes without one and the server decides: a log-only server lets it through, an enforcing one answers 401, and that answer is
/// what the screen shows. The error is never swapped for a cached or offline substitute.
/// </para>
/// <para>
/// When the second attestation also fails, the caller gets a problem response naming why (<c>APP_ATTESTATION_FAILED</c> or
/// <c>APP_ATTESTATION_UNAVAILABLE</c>, or the server's own <c>APP_SESSION_REQUIRED</c>), so the screen can say so with its usual
/// Retry. If the server cannot be reached at all it is an <see cref="HttpRequestException"/>.
/// </para>
/// </remarks>
public sealed class AppSessionHandler : ReplayAfterRefreshHandler
{
    /// <summary>The English fallback for the problem title. The app translates by <c>code</c>.</summary>
    public const string VerificationFailedTitle = "Couldn't verify this app with Google Play.";

    private readonly AppSessionService _service;

    /// <summary>Creates the handler.</summary>
    /// <param name="service">The session service the calls take their session from.</param>
    public AppSessionHandler(AppSessionService service)
        : base("Ballast.Maui.AppAttestation.Replayed", "[AppSession]")
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <inheritdoc />
    protected override async Task<string?> AttachAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _service.GetTokenAsync(ct).ConfigureAwait(false);
        if (token is not null)
            SetCredential(request, token);

        return token;
    }

    /// <inheritdoc />
    protected override Task<bool> IsRefusalAsync(HttpResponseMessage response, string? sentCredential, CancellationToken ct) =>
        SaysSessionRequiredAsync(response, ct);

    /// <inheritdoc />
    protected override Task<string?> RenewAsync(string? rejectedCredential, CancellationToken ct) =>
        _service.RefreshAsync(rejectedCredential, ct);

    /// <inheritdoc />
    protected override void SetCredential(HttpRequestMessage request, string credential)
    {
        request.Headers.Remove(AppSessionService.HeaderName);
        request.Headers.TryAddWithoutValidation(AppSessionService.HeaderName, credential);
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> RenewFailedAsync(HttpRequestMessage request, HttpResponseMessage response, CancellationToken ct)
    {
        var failure = _service.LastFailure;
        Trace.WriteLine($"[AppSession] The server wants an app session and attesting again failed ({failure?.Kind} {failure?.Code}).");
        response.Dispose();
        if (failure?.Kind == AppSessionFailureKind.Unreachable)
            throw new HttpRequestException(VerificationFailedTitle);

        return Task.FromResult(VerificationFailedResponse(request, failure));
    }

    /// <summary>True when the 401 is the server's <c>APP_SESSION_REQUIRED</c>, not some other 401. The body is buffered so the caller can still read it.</summary>
    private static async Task<bool> SaysSessionRequiredAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
            var problem = await ProblemReader.TryReadAsync(response, ct).ConfigureAwait(false);
            return string.Equals(problem?.Code, AppSessionCodes.SessionRequired, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is HttpRequestException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static HttpResponseMessage VerificationFailedResponse(HttpRequestMessage request, AppSessionFailure? failure)
    {
        var (status, code) = failure?.Kind switch
        {
            AppSessionFailureKind.Refused => (HttpStatusCode.Forbidden, failure.Code),
            AppSessionFailureKind.Unavailable => (HttpStatusCode.ServiceUnavailable, failure.Code),
            _ => (HttpStatusCode.Unauthorized, AppSessionCodes.SessionRequired),
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("title", VerificationFailedTitle);
            writer.WriteNumber("status", (int)status);
            writer.WriteString("code", code);
            writer.WriteEndObject();
        }

        return new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = new StringContent(Encoding.UTF8.GetString(stream.ToArray()), Encoding.UTF8, "application/problem+json"),
        };
    }
}
