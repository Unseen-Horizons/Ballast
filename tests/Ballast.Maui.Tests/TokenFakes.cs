// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Auth;

namespace Ballast.Maui.Tests;

/// <summary>An in-memory token store. It counts the writes and clears, so a test can see whether the tokens were touched.</summary>
internal sealed class MemoryTokenStore : ITokenStore
{
    /// <summary>The stored pair, or null when nothing is stored.</summary>
    public TokenPair? Pair { get; private set; }

    /// <summary>How many times the pair was written.</summary>
    public int Writes { get; private set; }

    /// <summary>How many times the pair was cleared.</summary>
    public int Clears { get; private set; }

    /// <summary>Puts a pair in place without counting it as a write, for the test's set-up.</summary>
    public void Seed(TokenPair? pair) => Pair = pair;

    /// <inheritdoc />
    public Task<string?> ReadAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(Pair?.AccessToken);

    /// <inheritdoc />
    public Task<string?> ReadRefreshTokenAsync(CancellationToken ct = default) => Task.FromResult(Pair?.RefreshToken);

    /// <inheritdoc />
    public Task WriteAsync(TokenPair pair, CancellationToken ct = default)
    {
        Pair = pair;
        Writes++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default)
    {
        Pair = null;
        Clears++;
        return Task.CompletedTask;
    }
}

/// <summary>A refresh call the test answers. It records each refresh token it was asked with.</summary>
internal sealed class ScriptedRefreshCall : ITokenRefreshCall
{
    /// <summary>The answer for a refresh token. Rejected by default.</summary>
    public Func<string, Task<TokenRefreshReply>> Answer { get; set; } = _ => Task.FromResult(new TokenRefreshReply(RefreshOutcome.Rejected));

    /// <summary>Every refresh token the call was made with, in order.</summary>
    public List<string> Sent { get; } = [];

    /// <inheritdoc />
    public Task<TokenRefreshReply> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        Sent.Add(refreshToken);
        return Answer(refreshToken);
    }
}

/// <summary>The session hooks, recorded. For the drop hook it notes whether the tokens were still stored when it ran.</summary>
internal sealed class RecordingSessionHooks(MemoryTokenStore? store = null) : ISessionHooks
{
    /// <summary>What happened, in order: "dropped:stored", "dropped:cleared" or "signed-out".</summary>
    public List<string> Events { get; } = [];

    /// <inheritdoc />
    public Task TokensDroppedAsync(CancellationToken ct = default)
    {
        Events.Add(store is { Pair: not null } ? "dropped:stored" : "dropped:cleared");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SignedOutAsync(CancellationToken ct = default)
    {
        Events.Add("signed-out");
        return Task.CompletedTask;
    }
}

/// <summary>An API the test scripts. It records each request's bearer parameter and body, then answers with the test's function.</summary>
internal sealed class ScriptedApi(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    /// <summary>The bearer parameter each request carried, or null when it carried none.</summary>
    public List<string?> Authorizations { get; } = [];

    /// <summary>The body each request carried ("" when none).</summary>
    public List<string> Bodies { get; } = [];

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Authorizations.Add(request.Headers.Authorization?.Parameter);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return respond(request);
    }
}
