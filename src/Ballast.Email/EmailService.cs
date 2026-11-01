// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Ballast.Email;

/// <summary>
/// Decides what a send means for the settings, and asks the transport to send when sending is on. It is the one place that
/// turns the settings into an <see cref="EmailSendResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// With an API key, the transport sends the mail. Without one, the mail is written to the log when logging is on (by default
/// in Development, where the subject and the text are logged so a flow can be followed without a provider) and is otherwise
/// reported as <see cref="EmailSendResult.NotConfigured"/>. A key without a sender, or a key with no transport registered, is
/// also <see cref="EmailSendResult.NotConfigured"/>, and the log names the setting.
/// </para>
/// <para>
/// Addresses in the log are masked with <see cref="EmailAddress.Mask"/>. The subject and the text are logged only by the
/// mock mail, which runs only when logging is on.
/// The HTML part and the key are never logged.
/// </para>
/// </remarks>
public class EmailService
{
    private readonly MailSettings _settings;
    private readonly bool _logWhenNotConfigured;
    private readonly ILogger<EmailService> _logger;
    private readonly IMailTransport? _transport;

    /// <summary>Creates the service.</summary>
    /// <param name="settings">The mail settings.</param>
    /// <param name="logger">The logger for the outcomes that are not a plain success.</param>
    /// <param name="transport">The transport that sends, or null when none is registered.</param>
    /// <param name="isDevelopment">
    /// The default for logging the mail instead of sending it, used when <see cref="MailSettings.LogInsteadOfSending"/> is not set.
    /// Pass true in Development.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> or <paramref name="logger"/> is null.</exception>
    public EmailService(MailSettings settings, ILogger<EmailService> logger, IMailTransport? transport = null, bool isDevelopment = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _settings = settings;
        _logger = logger;
        _transport = transport;
        _logWhenNotConfigured = settings.LogInsteadOfSending ?? isDevelopment;
    }

    /// <summary>
    /// Sends one e-mail, or reports why it was not sent. Only cancellation is thrown: any other failure is logged and reported
    /// as <see cref="EmailSendResult.Failed"/>, and the caller decides what that means for its own record.
    /// </summary>
    /// <param name="to">The recipient's address.</param>
    /// <param name="subject">The subject line.</param>
    /// <param name="text">The plain-text body.</param>
    /// <param name="html">The HTML body, or null for a plain-text e-mail.</param>
    /// <param name="ct">Cancels the send. A cancelled send throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>What happened to the e-mail.</returns>
    /// <exception cref="ArgumentException"><paramref name="to"/> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="subject"/> or <paramref name="text"/> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled during the send.</exception>
    public virtual async Task<EmailSendResult> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(text);

        var masked = EmailAddress.Mask(to);
        if (!_settings.IsConfigured)
        {
            if (_logWhenNotConfigured)
            {
                _logger.LogInformation("--- MOCK EMAIL (no {Section}:ApiKey) ---\nTo: {To}\nSubject: {Subject}\n\n{Body}\n--- END MOCK EMAIL ---", _settings.SectionName, masked, subject, text);
                return EmailSendResult.Logged;
            }

            _logger.LogWarning("{Section}:ApiKey is not set, so the e-mail to {To} was not sent.", _settings.SectionName, masked);
            return EmailSendResult.NotConfigured;
        }

        var missing = _settings.MissingSettings;
        if (missing.Count > 0)
        {
            _logger.LogError("{Missing} is not set, so the e-mail to {To} was not sent.", string.Join(", ", missing), masked);
            return EmailSendResult.NotConfigured;
        }

        if (_transport is null)
        {
            _logger.LogError("{Section}:ApiKey is set and its settings are complete, but no mail transport is registered, so the e-mail to {To} was not sent.", _settings.SectionName, masked);
            return EmailSendResult.NotConfigured;
        }

        try
        {
            return await _transport.SendAsync(to, subject, text, html, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to send the e-mail to {To}.", masked);
            return EmailSendResult.Failed;
        }
    }
}
