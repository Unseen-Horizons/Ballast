// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Feedback;

/// <summary>
/// The result of sending feedback: the outcome, a code, and the fields of the server's problem answer when it gave one.
/// </summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Code">
/// The server's code when it answered with one, otherwise the fallback in <see cref="FeedbackCodes"/> for the outcome. Empty for
/// <see cref="FeedbackSendOutcome.Sent"/>.
/// </param>
/// <param name="ServerTitle">The server's title for the problem, or null. For the app's own wording only when there is no server title.</param>
/// <param name="ServerDetail">The server's detail for the problem, or null.</param>
/// <param name="TraceId">The server's trace reference, to match the app's log to the server's, or null.</param>
public sealed record FeedbackSendResult(
    FeedbackSendOutcome Outcome,
    string Code,
    string? ServerTitle = null,
    string? ServerDetail = null,
    string? TraceId = null);
