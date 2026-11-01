// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>
/// Sends one e-mail through a mail provider. <see cref="EmailService"/> decides whether to call it; the transport only sends,
/// and reports what the provider did. Only cancellation is thrown. Any other failure is logged by the transport, without the
/// address, the text or the key, and reported as <see cref="EmailSendResult.Failed"/>.
/// </summary>
public interface IMailTransport
{
    /// <summary>Sends one e-mail: a plain-text part, and an HTML part when one is given.</summary>
    /// <param name="to">The recipient's address.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="text">The plain-text body.</param>
    /// <param name="html">The HTML body, or null for a plain-text e-mail.</param>
    /// <param name="ct">Cancels the call. A cancelled call throws <see cref="OperationCanceledException"/>.</param>
    /// <returns><see cref="EmailSendResult.Sent"/> when the provider accepted the e-mail, otherwise <see cref="EmailSendResult.Failed"/>.</returns>
    Task<EmailSendResult> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default);
}
