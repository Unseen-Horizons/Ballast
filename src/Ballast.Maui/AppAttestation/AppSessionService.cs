// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;

using Ballast.Errors;

namespace Ballast.Maui.AppAttestation;

/// <summary>
/// Owns the app's session with the server: obtains one lazily, keeps it in memory, and gets a new one when it is about to end or
/// the server rejects it. Nothing is persisted, so a cold start attests once.
/// </summary>
/// <remarks>
/// <para>
/// A session is a token the server issues after the platform (Google Play, or a developer certificate for a developer build)
/// vouches for this install. <see cref="AppSessionHandler"/> adds it to the app's calls, and <see cref="GetWebTicketAsync"/> carries
/// it into a WebView.
/// </para>
/// <para>
/// Every attestation goes through one gate, so calls that start together share one attestation. A failed attempt is not repeated
/// for <see cref="FailureBackoff"/> unless the server has just rejected the session: a 401 is evidence, a timer is not. Pure, with no
/// MAUI APIs, so the test project pins its behaviour without a device.
/// </para>
/// </remarks>
public sealed class AppSessionService : IDisposable
{
    /// <summary>The header that carries the session on every call.</summary>
    public const string HeaderName = "X-App-Session";

    /// <summary>A session is replaced this long before it ends, so a call never starts with one about to lapse.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    /// <summary>After a failed attestation, calls go out without a session for this long before trying again.</summary>
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(2);

    private readonly IAppAttestation _attestation;
    private readonly HttpClient _http;
    private readonly Func<string> _installId;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// One issued session: the token and when it ends, published together. <see cref="GetTokenAsync"/> reads it without the gate,
    /// so the two must never be seen apart.
    /// </summary>
    private sealed record Session(string Token, DateTimeOffset ExpiresAt);

    // Written only with the gate held, always with Volatile.Write; read with Volatile.Read, one snapshot per decision.
    private Session? _session;
    private DateTimeOffset? _failedAt;

    /// <summary>Creates the service.</summary>
    /// <param name="attestation">The platform attestation.</param>
    /// <param name="http">The bootstrap client for the server, with no <see cref="AppSessionHandler"/> on it (the handler would need a session to get a session).</param>
    /// <param name="installId">A random id kept for this install. Only a hash of it is logged server-side.</param>
    /// <param name="clock">The clock for the renewal window. The system clock when null.</param>
    public AppSessionService(IAppAttestation attestation, HttpClient http, Func<string> installId, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(installId);
        _attestation = attestation;
        _http = http;
        _installId = installId;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>Why the last attestation failed, or null when the last one worked (or none was tried).</summary>
    public AppSessionFailure? LastFailure { get; private set; }

    /// <summary>The session has not ended.</summary>
    private bool IsUsable([NotNullWhen(true)] Session? session) =>
        session is not null && _clock.GetUtcNow() < session.ExpiresAt;

    /// <summary>The session is more than <see cref="RenewBefore"/> from ending, so it is used as it is.</summary>
    private bool IsFresh([NotNullWhen(true)] Session? session) =>
        session is not null && _clock.GetUtcNow() < session.ExpiresAt - RenewBefore;

    private bool InBackoff => _failedAt is { } at && _clock.GetUtcNow() - at < FailureBackoff;

    /// <summary>The current session's token while it has not ended, else null.</summary>
    private string? UsableToken()
    {
        var session = Volatile.Read(ref _session);
        return IsUsable(session) ? session.Token : null;
    }

    /// <summary>
    /// The current session token, attesting first when there is none or it is within <see cref="RenewBefore"/> of ending. Null when
    /// no session could be had: the caller goes on without one and the server decides (a log-only server lets it through, an
    /// enforcing one answers 401).
    /// </summary>
    /// <param name="ct">Cancels the attestation.</param>
    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        // Without the gate: the token returned is the one the check was made on, from the same snapshot.
        var current = Volatile.Read(ref _session);
        if (IsFresh(current))
            return current.Token;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            current = Volatile.Read(ref _session);
            if (IsFresh(current))
                return current.Token;

            // A recent failure is not retried on every call; a token that has not actually ended is still good meanwhile.
            if (InBackoff)
                return UsableToken();

            await AttestLockedAsync(ct).ConfigureAwait(false);
            return UsableToken();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// A new session after the server rejected <paramref name="rejectedToken"/> with a 401. If another call has already replaced it,
    /// that one is returned and nothing is attested. Otherwise attests now, ignoring the failure backoff. Null when attestation
    /// failed (see <see cref="LastFailure"/>).
    /// </summary>
    /// <param name="rejectedToken">The token the server refused.</param>
    /// <param name="ct">Cancels the attestation.</param>
    public async Task<string?> RefreshAsync(string? rejectedToken, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref _session);
            if (IsUsable(current) && !string.Equals(current.Token, rejectedToken, StringComparison.Ordinal))
                return current.Token;

            // The rejected token is no use to anyone now, whatever its own expiry says.
            Volatile.Write(ref _session, null);
            await AttestLockedAsync(ct).ConfigureAwait(false);
            return UsableToken();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Attests again now, whatever the current session looks like. For a WebView whose cookie the server has refused: the session the
    /// app holds may be the very one the cookie carries, so it cannot be reused. Null when attestation failed (see <see cref="LastFailure"/>).
    /// </summary>
    /// <param name="ct">Cancels the attestation.</param>
    public Task<string?> ForceRenewAsync(CancellationToken ct = default) => RefreshAsync(Volatile.Read(ref _session)?.Token, ct);

    /// <summary>
    /// A sixty-second ticket the WebView redeems for its cookie. Gets a session first if there is none; one 401 is answered by attesting
    /// again and trying once more.
    /// </summary>
    /// <param name="ct">Cancels the calls.</param>
    public async Task<WebTicketResult> GetWebTicketAsync(CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);
        if (token is null)
            return new WebTicketResult(null, LastFailure ?? new AppSessionFailure(AppSessionFailureKind.Other, AppSessionCodes.SessionRequired, "No app session."));

        var (ticket, status) = await PostForTicketAsync(token, ct).ConfigureAwait(false);
        if (ticket is null && status == HttpStatusCode.Unauthorized)
        {
            token = await RefreshAsync(token, ct).ConfigureAwait(false);
            if (token is null)
                return new WebTicketResult(null, LastFailure ?? new AppSessionFailure(AppSessionFailureKind.Other, AppSessionCodes.SessionRequired, "No app session."));

            (ticket, status) = await PostForTicketAsync(token, ct).ConfigureAwait(false);
        }

        return ticket is not null
            ? new WebTicketResult(ticket, null)
            : new WebTicketResult(null, new AppSessionFailure(AppSessionFailureKind.Other, AppSessionCodes.SessionRequired, $"The server refused a web ticket ({(int)status})."));
    }

    private async Task<(string? Ticket, HttpStatusCode Status)> PostForTicketAsync(string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "app/session/web-ticket");
            request.Headers.TryAddWithoutValidation(HeaderName, token);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var reply = JsonSerializer.Deserialize(body, AppSessionJson.Default.AppWebTicketDto);
            var ticket = reply?.Ticket;
            return (string.IsNullOrWhiteSpace(ticket) ? null : ticket, response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            Trace.WriteLine($"[AppSession] web ticket request failed: {ex.GetType().Name}: {ex.Message}");
            return (null, HttpStatusCode.ServiceUnavailable);
        }
    }

    /// <summary>One attestation, with the gate held: nonce, platform token, session. Sets the token or <see cref="LastFailure"/>.</summary>
    private async Task AttestLockedAsync(CancellationToken ct)
    {
        try
        {
            var nonce = await GetNonceAsync(ct).ConfigureAwait(false);
            if (nonce is null)
                return;

            string? integrityToken = null;
            try
            {
                integrityToken = await _attestation.GetTokenAsync(nonce, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The provider is meant to return null, not throw; if it throws the server is still asked and decides.
                Trace.WriteLine($"[AppSession] The platform attestation failed ({ex.GetType().Name}: {ex.Message}); asking the server without a token.");
            }

            await CreateSessionAsync(nonce, integrityToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            Fail(new AppSessionFailure(AppSessionFailureKind.Unreachable, "NETWORK_UNREACHABLE", ex.Message));
        }
        catch (JsonException ex)
        {
            Fail(new AppSessionFailure(AppSessionFailureKind.Other, "SERVER_UNAVAILABLE", $"The server's answer was not readable: {ex.Message}"));
        }
    }

    private async Task<string?> GetNonceAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("app/session/nonce", ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Fail(await FailureFromAsync(response, ct).ConfigureAwait(false));
            return null;
        }

        var reply = JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), AppSessionJson.Default.AppNonceDto);
        var nonce = reply?.Nonce;
        if (string.IsNullOrWhiteSpace(nonce))
        {
            Fail(new AppSessionFailure(AppSessionFailureKind.Other, "SERVER_UNAVAILABLE", "The server sent no nonce."));
            return null;
        }

        return nonce;
    }

    private async Task CreateSessionAsync(string nonce, string? integrityToken, CancellationToken ct)
    {
        var request = new AppSessionRequestDto(_attestation.Platform, _installId(), nonce, integrityToken);
        using var content = new StringContent(JsonSerializer.Serialize(request, AppSessionJson.Default.AppSessionRequestDto), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("app/session", content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Fail(await FailureFromAsync(response, ct).ConfigureAwait(false));
            return;
        }

        var reply = JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), AppSessionJson.Default.AppSessionDto);
        var token = reply?.AccessToken;
        var seconds = reply?.ExpiresInSeconds ?? 0;
        if (string.IsNullOrWhiteSpace(token) || seconds <= 0)
        {
            Fail(new AppSessionFailure(AppSessionFailureKind.Other, "SERVER_UNAVAILABLE", "The server sent no usable session."));
            return;
        }

        // One write: a caller on the fast path sees the old session or this one, never the token without its expiry.
        Volatile.Write(ref _session, new Session(token, _clock.GetUtcNow().AddSeconds(seconds)));
        _failedAt = null;
        LastFailure = null;
        Trace.WriteLine($"[AppSession] A session was issued; it lasts {seconds / 3600.0:F1} h.");
    }

    private static async Task<AppSessionFailure> FailureFromAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var problem = await ProblemReader.TryReadAsync(response, ct).ConfigureAwait(false);
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Forbidden => AppSessionFailureKind.Refused,
            HttpStatusCode.ServiceUnavailable => AppSessionFailureKind.Unavailable,
            _ => AppSessionFailureKind.Other,
        };

        var code = problem?.Code ?? kind switch
        {
            AppSessionFailureKind.Refused => AppSessionCodes.AttestationFailed,
            AppSessionFailureKind.Unavailable => AppSessionCodes.AttestationUnavailable,
            _ => "SERVER_UNAVAILABLE",
        };

        return new AppSessionFailure(kind, code, problem?.Title ?? $"HTTP {(int)response.StatusCode}");
    }

    private void Fail(AppSessionFailure failure)
    {
        LastFailure = failure;
        _failedAt = _clock.GetUtcNow();
        Trace.WriteLine($"[AppSession] No session: {failure.Kind} {failure.Code} {failure.Message}");
    }
}
