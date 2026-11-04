// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Feedback;

/// <summary>
/// How an attempt to send feedback ended. The app supplies the words it shows for each outcome; the library names none.
/// </summary>
public enum FeedbackSendOutcome
{
    /// <summary>The server took the message (a 204).</summary>
    Sent,

    /// <summary>There was nothing to send: the message was blank. Nothing was posted.</summary>
    Empty,

    /// <summary>The server has nowhere to send it (a 501). A problem with the deployment, not with the user.</summary>
    NotConfigured,

    /// <summary>The server has no feedback endpoint to answer (a 404).</summary>
    NotAvailable,

    /// <summary>The server refused the message (any other error status).</summary>
    Rejected,

    /// <summary>The server could not be reached: no connection, or the call timed out. The message is still in the app, to send again.</summary>
    Unreachable,
}
