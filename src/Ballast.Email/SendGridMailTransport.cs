// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Ballast.Email;

/// <summary>
/// Sends through SendGrid's web API (<c>POST https://api.sendgrid.com/v3/mail/send</c>) with an <see cref="HttpClient"/>. No
/// SendGrid SDK is used: the request is one small JSON document, written here, and the <c>errors</c> array of an answer is read
/// here. Every request turns open and click tracking off, because tracking would rewrite the sign-in and confirmation links
/// through SendGrid's redirect domain. The plain-text part comes first, and an HTML part is added only when one is given.
/// </summary>
/// <remarks>
/// <para>
/// A failure is not thrown. A refused request, a timeout and a connection failure are all <see cref="EmailSendResult.Failed"/>,
/// and the transport logs the status and SendGrid's messages. Only a cancelled caller token is thrown.
/// </para>
/// <para>
/// Nothing the transport logs holds the subject, the text, the HTML, the key or the full address. An address appears as
/// <see cref="EmailAddress.Mask"/> writes it, and any copy of it inside a SendGrid message is masked the same way.
/// </para>
/// </remarks>
public sealed class SendGridMailTransport : IMailTransport
{
    /// <summary>The SendGrid endpoint that sends one e-mail.</summary>
    public const string Endpoint = "https://api.sendgrid.com/v3/mail/send";

    // The JSON is sent over HTTP, not written into a page, so the HTML-safe escaping is not needed. The relaxed encoder keeps
    // non-ASCII text (names with diacritics, for example) as UTF-8, and escapes only what JSON requires.
    private static readonly JsonWriterOptions _jsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string? _fromName;
    private readonly ILogger<SendGridMailTransport> _logger;

    /// <summary>
    /// Creates the transport. The client's timeout is the transport's timeout, so the app sets it on the client it registers.
    /// </summary>
    /// <param name="http">The client that sends the request. It is not disposed by the transport.</param>
    /// <param name="settings">The settings, which must hold an API key and a sender address.</param>
    /// <param name="logger">The logger for refusals and failures.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="ArgumentException">The API key or the sender address is not set. The message names the setting.</exception>
    public SendGridMailTransport(HttpClient http, MailSettings settings, ILogger<SendGridMailTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _apiKey = settings.ApiKey ?? throw new ArgumentException($"{settings.SectionName}:ApiKey is required.", nameof(settings));
        _fromEmail = settings.FromEmail ?? throw new ArgumentException($"{settings.SectionName}:FromEmail is required.", nameof(settings));
        _fromName = settings.FromName;
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="to"/> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="subject"/> or <paramref name="text"/> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled. A timeout of the client is not this exception: it is a failure.</exception>
    public async Task<EmailSendResult> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(text);
        ct.ThrowIfCancellationRequested();

        var masked = EmailAddress.Mask(to);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new ByteArrayContent(BuildRequest(to, subject, text, html)),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return EmailSendResult.Sent;
            }

            // The subject and the text are never logged: the text holds sign-in links and customer details.
            var messages = await ReadErrorMessagesAsync(response, to, masked, ct);
            var status = (int)response.StatusCode;
            if (messages.Count == 0)
            {
                _logger.LogError("SendGrid returned {StatusCode} for the e-mail to {To}, so it was not sent.", status, masked);
            }
            else
            {
                _logger.LogError("SendGrid returned {StatusCode} for the e-mail to {To}, so it was not sent: {SendGridErrors}", status, masked, string.Join(" ", messages));
            }

            return EmailSendResult.Failed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send the e-mail to {To}.", masked);
            return EmailSendResult.Failed;
        }
    }

    /// <summary>
    /// The request body. Tracking is off, the sender is the configured one (with its name when there is one), and the content
    /// is the plain-text part, followed by the HTML part when one is given.
    /// </summary>
    private byte[] BuildRequest(string to, string subject, string text, string? html)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, _jsonOptions))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("from");
            writer.WriteString("email", _fromEmail);
            if (_fromName is not null)
            {
                writer.WriteString("name", _fromName);
            }

            writer.WriteEndObject();
            writer.WriteString("subject", subject);

            writer.WriteStartArray("personalizations");
            writer.WriteStartObject();
            writer.WriteStartArray("to");
            writer.WriteStartObject();
            writer.WriteString("email", to);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            // SendGrid takes the plain-text part first and the HTML part after it.
            writer.WriteStartArray("content");
            WriteContent(writer, "text/plain", text);
            if (html is not null)
            {
                WriteContent(writer, "text/html", html);
            }

            writer.WriteEndArray();

            writer.WriteStartObject("tracking_settings");
            writer.WriteStartObject("click_tracking");
            writer.WriteBoolean("enable", false);
            writer.WriteBoolean("enable_text", false);
            writer.WriteEndObject();
            writer.WriteStartObject("open_tracking");
            writer.WriteBoolean("enable", false);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteContent(Utf8JsonWriter writer, string type, string value)
    {
        writer.WriteStartObject();
        writer.WriteString("type", type);
        writer.WriteString("value", value);
        writer.WriteEndObject();
    }

    /// <summary>
    /// The messages of SendGrid's <c>{"errors":[{"message":...}]}</c>. Empty when the body is not that JSON, and the caller then
    /// logs the status alone. A copy of the recipient's address inside a message is replaced with its masked form.
    /// </summary>
    private static async Task<List<string>> ReadErrorMessagesAsync(HttpResponseMessage response, string to, string masked, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        List<string> messages = [];
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            {
                return messages;
            }

            foreach (var error in errors.EnumerateArray())
            {
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && message.GetString() is { } errorMessage)
                {
                    messages.Add(errorMessage.Replace(to, masked, StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        catch (JsonException)
        {
            // Not SendGrid's JSON: nothing to add, and the status is logged by the caller.
            return [];
        }

        return messages;
    }
}
