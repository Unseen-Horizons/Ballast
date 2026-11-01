// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Ballast.Email.Tests;

/// <summary>
/// The SendGrid send, with a fake HTTP handler: no SendGrid account is used. The request is compared as the exact JSON that is
/// posted, the responses are the ones SendGrid answers with, and the log is checked for what it must and must not hold.
/// </summary>
public class SendGridMailTransportTests
{
    private const string ApiKey = "SG.not-a-real-key";
    private const string FromEmail = "info@example.test";
    private const string FromName = "Example";
    private const string To = "klijent@example.test";
    private const string Subject = "Sign in to Example";
    private const string Text = "Click this link to sign in: https://example.test/portal/magic-link.html?token=secret.token.value";

    private static readonly MailSettings Settings = new(ApiKey, FromEmail, FromName);

    private static (SendGridMailTransport Transport, CapturingLogger<SendGridMailTransport> Logger, FakeSendGridHandler Handler) Create(
        FakeSendGridHandler handler, MailSettings? settings = null)
    {
        var logger = new CapturingLogger<SendGridMailTransport>();
        var transport = new SendGridMailTransport(new HttpClient(handler, disposeHandler: false), settings ?? Settings, logger);
        return (transport, logger, handler);
    }

    private static HttpResponseMessage Accepted() => new(HttpStatusCode.Accepted);

    private static HttpResponseMessage Error(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void Endpoint_IsSendGridsSendAddress()
    {
        Assert.Equal("https://api.sendgrid.com/v3/mail/send", SendGridMailTransport.Endpoint);
    }

    [Fact]
    public async Task SendAsync_PostsToSendGridWithTheBearerKey_AsJson()
    {
        var (transport, _, handler) = Create(new FakeSendGridHandler(Accepted()));

        await transport.SendAsync(To, Subject, Text);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendGridMailTransport.Endpoint, request.Url.AbsoluteUri);
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);
        Assert.Equal("application/json", request.ContentType);
    }

    [Fact]
    public async Task SendAsync_WritesTheExactRequest_ForTextOnly()
    {
        var (transport, _, handler) = Create(new FakeSendGridHandler(Accepted()));

        await transport.SendAsync("klijent@example.test", "Vaša narudžba", "Hello");

        const string expected = """
            {"from":{"email":"info@example.test","name":"Example"},"subject":"Vaša narudžba","personalizations":[{"to":[{"email":"klijent@example.test"}]}],"content":[{"type":"text/plain","value":"Hello"}],"tracking_settings":{"click_tracking":{"enable":false,"enable_text":false},"open_tracking":{"enable":false}}}
            """;
        Assert.Equal(expected, Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task SendAsync_WithAnHtmlBody_PutsThePlainTextPartFirst()
    {
        var (transport, _, handler) = Create(new FakeSendGridHandler(Accepted()));

        await transport.SendAsync("klijent@example.test", "Vaša narudžba", "Hello", "<p>Hello</p>");

        const string expected = """
            {"from":{"email":"info@example.test","name":"Example"},"subject":"Vaša narudžba","personalizations":[{"to":[{"email":"klijent@example.test"}]}],"content":[{"type":"text/plain","value":"Hello"},{"type":"text/html","value":"<p>Hello</p>"}],"tracking_settings":{"click_tracking":{"enable":false,"enable_text":false},"open_tracking":{"enable":false}}}
            """;
        Assert.Equal(expected, Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task SendAsync_WithoutAFromName_LeavesTheNameOut()
    {
        var (transport, _, handler) = Create(new FakeSendGridHandler(Accepted()), new MailSettings(ApiKey, FromEmail));

        await transport.SendAsync(To, Subject, "Hello");

        using var json = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var from = json.RootElement.GetProperty("from");
        Assert.Equal(FromEmail, from.GetProperty("email").GetString());
        Assert.False(from.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task SendAsync_WhenSendGridAccepts_ReportsSent_AndLogsNothing()
    {
        var (transport, logger, _) = Create(new FakeSendGridHandler(Accepted()));

        var result = await transport.SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Sent, result);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(400, "The from address does not match a verified Sender Identity.")]
    [InlineData(401, "The provided authorization grant is invalid, expired, or revoked.")]
    [InlineData(403, "Access to the Mail Send endpoint is forbidden for this key.")]
    public async Task SendAsync_WhenSendGridRefuses_ReportsFailed_AndLogsTheStatusAndMessages_NotTheTextOrKey(int status, string message)
    {
        var errorJson = JsonSerializer.Serialize(new { errors = new[] { new { message, field = (string?)null, help = (string?)null } } });
        var (transport, logger, handler) = Create(new FakeSendGridHandler(Error((HttpStatusCode)status, errorJson)));

        var result = await transport.SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Failed, result);
        Assert.Single(handler.Requests);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Message);
        Assert.Contains(message, entry.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(Text) || e.Message.Contains("token=") || e.Message.Contains(Subject) || e.Message.Contains(ApiKey));
    }

    [Fact]
    public async Task SendAsync_NeverLogsTheFullAddress()
    {
        var errorJson = JsonSerializer.Serialize(new { errors = new[] { new { message = "Invalid recipient klijent@example.test." } } });
        var (transport, logger, _) = Create(new FakeSendGridHandler(Error(HttpStatusCode.BadRequest, errorJson)));

        await transport.SendAsync(To, Subject, Text);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("k***@example.test", entry.Message);
        Assert.DoesNotContain(To, entry.Message);
    }

    [Fact]
    public async Task SendAsync_WhenTheErrorIsNotSendGridsJson_LogsTheStatusOnly()
    {
        var (transport, logger, _) = Create(new FakeSendGridHandler(Error(HttpStatusCode.InternalServerError, "<html>gateway page</html>")));

        var result = await transport.SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Failed, result);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("500", entry.Message);
        Assert.DoesNotContain("gateway page", entry.Message);
    }

    [Fact]
    public async Task SendAsync_WhenTheHttpCallThrows_ReportsFailed_AndLogsTheException()
    {
        var (transport, logger, _) = Create(new FakeSendGridHandler(Accepted()) { ThrowOnSend = new HttpRequestException("The connection was reset.") });

        var result = await transport.SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Failed, result);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<HttpRequestException>(entry.Exception);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(Text));
    }

    [Fact]
    public async Task SendAsync_WhenTheClientTimesOut_ReportsFailed_BecauseOnlyACallerCancellationIsRethrown()
    {
        var (transport, logger, _) = Create(new FakeSendGridHandler(Accepted()) { ThrowOnSend = new TaskCanceledException("The request timed out.") });

        var result = await transport.SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Failed, result);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<TaskCanceledException>(entry.Exception);
    }

    [Fact]
    public async Task SendAsync_WhenTheTokenIsCancelledBeforeTheCall_ThrowsWithoutSending()
    {
        var (transport, _, handler) = Create(new FakeSendGridHandler(Accepted()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendAsync(To, Subject, Text, ct: cts.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_WhenTheCallerCancelsDuringTheCall_RethrowsCancellation()
    {
        using var cts = new CancellationTokenSource();
        var (transport, logger, _) = Create(new FakeSendGridHandler(Accepted()) { CancelOnSend = cts });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendAsync(To, Subject, Text, ct: cts.Token));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task SendAsync_WithABlankRecipient_Throws()
    {
        var (transport, _, handler) = Create(new FakeSendGridHandler(Accepted()));

        await Assert.ThrowsAsync<ArgumentException>(() => transport.SendAsync("  ", Subject, Text));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Constructor_WithoutAKey_Throws_NamingTheSetting()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SendGridMailTransport(
            new HttpClient(new FakeSendGridHandler(), disposeHandler: false),
            new MailSettings(apiKey: null, fromEmail: FromEmail),
            new CapturingLogger<SendGridMailTransport>()));

        Assert.Contains("SendGrid:ApiKey", exception.Message);
    }

    [Fact]
    public void Constructor_WithoutASender_Throws_NamingTheSetting()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SendGridMailTransport(
            new HttpClient(new FakeSendGridHandler(), disposeHandler: false),
            new MailSettings(apiKey: ApiKey, fromEmail: null, sectionName: "Mail"),
            new CapturingLogger<SendGridMailTransport>()));

        Assert.Contains("Mail:FromEmail", exception.Message);
    }
}
