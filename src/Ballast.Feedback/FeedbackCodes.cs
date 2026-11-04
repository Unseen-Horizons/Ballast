// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Feedback;

/// <summary>
/// The codes the client gives an outcome when the server gave no code of its own. They are the names the app has always logged,
/// so they keep their text.
/// </summary>
public static class FeedbackCodes
{
    /// <summary>The fallback for <see cref="FeedbackSendOutcome.Empty"/>.</summary>
    public const string Empty = "FeedbackEmpty";

    /// <summary>The fallback for <see cref="FeedbackSendOutcome.NotConfigured"/>.</summary>
    public const string NotConfigured = "FeedbackNotConfigured";

    /// <summary>The fallback for <see cref="FeedbackSendOutcome.NotAvailable"/>.</summary>
    public const string NotAvailable = "FeedbackNotAvailable";

    /// <summary>The fallback for <see cref="FeedbackSendOutcome.Rejected"/>.</summary>
    public const string Rejected = "FeedbackRejected";

    /// <summary>The fallback for <see cref="FeedbackSendOutcome.Unreachable"/>.</summary>
    public const string Unreachable = "FeedbackUnreachable";
}
