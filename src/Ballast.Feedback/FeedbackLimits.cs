// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Feedback;

/// <summary>
/// The caps on a feedback request, in characters. They are caps rather than expectations: feedback arrives on an endpoint that
/// anyone may call, and an unbounded text field there is an invitation. A value over a cap is refused, never cut down.
/// </summary>
public static class FeedbackLimits
{
    /// <summary>The longest message.</summary>
    public const int MessageMaxLength = 4000;

    /// <summary>The longest subject.</summary>
    public const int SubjectMaxLength = 200;

    /// <summary>The longest reply address.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>The longest app version text.</summary>
    public const int AppVersionMaxLength = 64;

    /// <summary>The longest platform text.</summary>
    public const int PlatformMaxLength = 64;

    /// <summary>The longest category.</summary>
    public const int CategoryMaxLength = 64;

    /// <summary>The longest context name.</summary>
    public const int ContextNameMaxLength = 200;
}
