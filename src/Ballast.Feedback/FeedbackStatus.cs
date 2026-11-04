// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Feedback;

/// <summary>
/// Where a feedback message is in triage. A new message is <see cref="New"/>. The store keeps the name (<c>ToString()</c>), so a
/// status keeps its meaning in storage and on the wire; the members and their order do not change.
/// </summary>
public enum FeedbackStatus
{
    /// <summary>Received and not yet looked at.</summary>
    New,

    /// <summary>Someone is looking at it.</summary>
    InReview,

    /// <summary>Dealt with.</summary>
    Handled,

    /// <summary>Looked at and not acted on.</summary>
    Dismissed,
}
