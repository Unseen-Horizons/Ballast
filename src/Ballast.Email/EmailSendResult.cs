// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>
/// What happened to an e-mail. Only <see cref="Sent"/> and <see cref="Logged"/> count as delivered. The details of a failure
/// (the HTTP status and the provider's messages) are logged by the transport, never with the address, the text or the key.
/// </summary>
public enum EmailSendResult
{
    /// <summary>The mail provider accepted the e-mail.</summary>
    Sent,

    /// <summary>
    /// Sending is off, and logging the e-mail instead is on (by default in Development). The e-mail was written to the log, so
    /// it counts as delivered, and local work can follow a flow without a provider.
    /// </summary>
    Logged,

    /// <summary>
    /// Sending is off and logging is off, the settings are incomplete, or no transport is registered: nothing was sent.
    /// </summary>
    NotConfigured,

    /// <summary>The provider refused the e-mail or the call failed: nothing is known to have been delivered.</summary>
    Failed,
}

/// <summary>Helpers for <see cref="EmailSendResult"/>.</summary>
public static class EmailSendResultExtensions
{
    /// <summary>True for <see cref="EmailSendResult.Sent"/> and <see cref="EmailSendResult.Logged"/>, and false for the others.</summary>
    /// <param name="result">The result to test.</param>
    /// <returns>True when the e-mail counts as delivered.</returns>
    public static bool IsDelivered(this EmailSendResult result) => result is EmailSendResult.Sent or EmailSendResult.Logged;
}
