// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Net.Http.Headers;

namespace Ballast.Maui.Auth;

/// <summary>
/// Puts the bearer token on every call to the app's server. A token that ends within five minutes is renewed before the call goes
/// out, and a 401 is answered by renewing once and sending the call again.
/// </summary>
/// <remarks>
/// Only a refused refresh token ends the session (<see cref="ISessionHooks.SignedOutAsync"/>). A busy or unreachable server is no
/// verdict, so the call fails as an ordinary failure and the rider stays signed in. A 401 on a call that carried no token is not an
/// expired session, so it is returned as it came.
/// </remarks>
public sealed class BearerTokenHandler : ReplayAfterRefreshHandler
{
    /// <summary>A token that ends within this long is renewed before the call goes out.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    private readonly TokenSession _session;
    private readonly TimeProvider _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="session">The token session, which also carries the sign-out hooks.</param>
    /// <param name="clock">The clock for the renewal window. The system clock when null.</param>
    public BearerTokenHandler(TokenSession session, TimeProvider? clock = null)
        : base("Ballast.Maui.Auth.BearerReplayed", "[BearerToken]")
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The renewal comes before the body check, so a refused call is still answered by the renewal it was owed.</summary>
    protected override bool RenewsBeforeCheckingBody => true;

    /// <inheritdoc />
    protected override async Task<string?> AttachAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _session.ReadAccessTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
            return null;

        if (JwtExpiry.EndsWithin(token, _clock.GetUtcNow(), RenewBefore))
        {
            Trace.WriteLine("[BearerToken] Token expiring soon, pre-emptively refreshing");
            if (await _session.RefreshAsync(ct).ConfigureAwait(false) == RefreshOutcome.Refreshed)
                token = await _session.ReadAccessTokenAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(token))
                return null;
        }

        SetCredential(request, token);
        return token;
    }

    /// <inheritdoc />
    protected override Task<bool> IsRefusalAsync(HttpResponseMessage response, string? sentCredential, CancellationToken ct)
    {
        // A 401 on a call that carried no token is not an expired session: there was no session to expire.
        if (string.IsNullOrEmpty(sentCredential))
        {
            Trace.WriteLine("[BearerToken] 401 with no token to begin with; leaving the session alone.");
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    protected override async Task<string?> RenewAsync(string? rejectedCredential, CancellationToken ct)
    {
        var outcome = await _session.RefreshAsync(ct).ConfigureAwait(false);
        if (outcome == RefreshOutcome.Rejected)
        {
            // Only a refused refresh token ends the session. The app signs the rider out, and the caller gets the 401 back.
            await _session.Hooks.SignedOutAsync(ct).ConfigureAwait(false);
            return null;
        }

        if (outcome == RefreshOutcome.Unavailable)
        {
            Trace.WriteLine("[BearerToken] Refresh was not answered; keeping the session and returning the 401.");
            return null;
        }

        return await _session.ReadAccessTokenAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void SetCredential(HttpRequestMessage request, string credential) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
}
