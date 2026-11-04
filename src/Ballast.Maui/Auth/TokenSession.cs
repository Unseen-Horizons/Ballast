// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace Ballast.Maui.Auth;

/// <summary>
/// The device's token session: reads the stored access token, and refreshes the pair. A refresh is serialised: one call is in
/// flight, and the others wait on it. Each waiter re-reads the stored refresh token under the lock, so a refresh another call
/// has just made is not repeated.
/// </summary>
/// <remarks>
/// The server rotates the refresh token and revokes the old one. Two calls that presented the same token would leave the second
/// holding a replayed token, which the server reads as theft and answers by ending every session the user has.
/// </remarks>
public sealed class TokenSession : IDisposable
{
    private readonly ITokenStore _store;
    private readonly ITokenRefreshCall _call;
    private readonly ISessionHooks _hooks;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the session.</summary>
    /// <param name="store">Where the tokens are kept.</param>
    /// <param name="call">The app's refresh call.</param>
    /// <param name="hooks">The app's sign-out work, or null for none.</param>
    public TokenSession(ITokenStore store, ITokenRefreshCall call, ISessionHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(call);
        _store = store;
        _call = call;
        _hooks = hooks ?? new NoSessionHooks();
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>The stored access token, or null when there is none.</summary>
    public Task<string?> ReadAccessTokenAsync(CancellationToken ct = default) => _store.ReadAccessTokenAsync(ct);

    /// <summary>
    /// Refreshes the pair and reports what the server said. Only a refusal (<see cref="RefreshOutcome.Rejected"/>) is a reason to end
    /// the session. A transport failure or a non-401 reply (408, 429, 5xx) is <see cref="RefreshOutcome.Unavailable"/>, and touches no
    /// stored token.
    /// </summary>
    /// <param name="ct">Cancels the wait for the lock and the call.</param>
    public async Task<RefreshOutcome> RefreshAsync(CancellationToken ct = default)
    {
        var observed = await _store.ReadRefreshTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(observed))
            return RefreshOutcome.Rejected;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await _store.ReadRefreshTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(current))
                return RefreshOutcome.Rejected;

            // Another call refreshed while this one waited; its new pair is already stored.
            if (current != observed)
                return RefreshOutcome.Refreshed;

            TokenRefreshReply reply;
            try
            {
                reply = await _call.RefreshAsync(current, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The server was unreachable, which says nothing about whether the token is still good. The stored tokens stay:
                // clearing them here would sign a rider out for walking into a tunnel.
                Trace.WriteLine($"[TokenSession] Could not reach the server to refresh: {ex.Message}");
                return RefreshOutcome.Unavailable;
            }

            if (reply.Outcome == RefreshOutcome.Unavailable)
            {
                Trace.WriteLine("[TokenSession] Refresh was not answered; keeping the session.");
                return reply.Outcome;
            }

            if (reply.Outcome == RefreshOutcome.Rejected)
                return reply.Outcome;

            if (reply.Pair is { } pair)
            {
                await _store.WriteAsync(pair, ct).ConfigureAwait(false);
                return RefreshOutcome.Refreshed;
            }

            // The server rotated the token but the reply was unreadable, so the replacement is lost. Keeping the old token would present
            // a replaced one on the next attempt, which the server reads as theft, so the session ends here. The app's data goes first,
            // because it may be derived from the tokens.
            Trace.WriteLine("[TokenSession] Refresh succeeded but the response could not be read; ending the session.");
            await _hooks.TokensDroppedAsync(ct).ConfigureAwait(false);
            await _store.ClearAsync(ct).ConfigureAwait(false);
            return RefreshOutcome.Rejected;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The sign-out hooks this session was made with, for the handler that acts on a refused refresh.</summary>
    internal ISessionHooks Hooks => _hooks;
}
