// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Auth;

/// <summary>The app's refresh call: presents the refresh token to its server and reads the answer.</summary>
public interface ITokenRefreshCall
{
    /// <summary>
    /// Presents <paramref name="refreshToken"/>. The outcome is what the reply means (<see cref="RefreshOutcomeRules.FromStatus"/> for
    /// the status). The pair is set when the reply carries a readable one.
    /// </summary>
    /// <remarks>
    /// A 2xx reply whose body cannot be read is <see cref="RefreshOutcome.Refreshed"/> with a null pair. The server has rotated
    /// the token, so the library ends the session rather than keep a token the server has replaced. A transport failure may
    /// throw <see cref="HttpRequestException"/> or <see cref="TaskCanceledException"/>; the library reads that as no verdict.
    /// </remarks>
    Task<TokenRefreshReply> RefreshAsync(string refreshToken, CancellationToken ct = default);
}

/// <summary>What a refresh call answered: the outcome, and the new pair when there is a readable one.</summary>
/// <param name="Outcome">The verdict on the refresh token.</param>
/// <param name="Pair">The new tokens, or null when the reply had none to read.</param>
public sealed record TokenRefreshReply(RefreshOutcome Outcome, TokenPair? Pair = null);
