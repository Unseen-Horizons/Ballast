// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Ballast.Email.Tests;

/// <summary>
/// What a send reports for each configuration. The transport is a fake here, so no test sends a real mail. The transport itself
/// is tested in <see cref="SendGridMailTransportTests"/>.
/// </summary>
public class EmailServiceTests
{
    private const string To = "klijent@example.test";
    private const string MaskedTo = "k***@example.test";
    private const string Subject = "Sign in to Example";
    private const string Text = "Click this link to sign in: https://example.test/portal/magic-link.html?token=secret.token.value";
    private const string ApiKey = "SG.not-a-real-key";
    private const string FromEmail = "info@example.test";

    private static EmailService Service(MailSettings settings, CapturingLogger<EmailService> logger, IMailTransport? transport = null, bool isDevelopment = false) =>
        new(settings, logger, transport, isDevelopment);

    private static MailSettings Keyed(string? fromEmail = FromEmail) => new(ApiKey, fromEmail, "Example");

    [Fact]
    public async Task SendAsync_WithoutAKeyInDevelopment_WritesTheMailToTheLog_AndCountsAsDelivered()
    {
        var logger = new CapturingLogger<EmailService>();

        var result = await Service(new MailSettings(null, FromEmail), logger, isDevelopment: true).SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Logged, result);
        Assert.True(result.IsDelivered());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(Text, entry.Message);
        Assert.Contains(MaskedTo, entry.Message);
        Assert.DoesNotContain(To, entry.Message);
    }

    [Fact]
    public async Task SendAsync_InDevelopment_TheMockMailNeverLogsTheHtmlPart()
    {
        var logger = new CapturingLogger<EmailService>();

        await Service(new MailSettings(null, FromEmail), logger, isDevelopment: true).SendAsync(To, Subject, Text, "<p>secret.html.part</p>");

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("secret.html.part"));
    }

    [Fact]
    public async Task SendAsync_WithoutAKeyOutsideDevelopment_IsNotConfigured_AndLogsOnlyTheMaskedRecipient()
    {
        var logger = new CapturingLogger<EmailService>();

        var result = await Service(new MailSettings(null, FromEmail), logger, isDevelopment: false).SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.NotConfigured, result);
        Assert.False(result.IsDelivered());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(MaskedTo, entry.Message);
        Assert.DoesNotContain(To, entry.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(Subject) || e.Message.Contains(Text) || e.Message.Contains("token"));
    }

    [Fact]
    public async Task SendAsync_WithLoggingSwitchedOff_InDevelopment_IsNotConfigured()
    {
        var logger = new CapturingLogger<EmailService>();
        var settings = new MailSettings(null, FromEmail, logInsteadOfSending: false);

        var result = await Service(settings, logger, isDevelopment: true).SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.NotConfigured, result);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(Text));
    }

    [Fact]
    public async Task SendAsync_WithLoggingSwitchedOn_OutsideDevelopment_LogsTheMail()
    {
        var logger = new CapturingLogger<EmailService>();
        var settings = new MailSettings(null, FromEmail, logInsteadOfSending: true);

        var result = await Service(settings, logger, isDevelopment: false).SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Logged, result);
        Assert.Contains(logger.Entries, e => e.Message.Contains(Text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_ABlankKey_CountsAsNoKey(string? apiKey)
    {
        var result = await Service(new MailSettings(apiKey, FromEmail), new CapturingLogger<EmailService>(), isDevelopment: true)
            .SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Logged, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendAsync_AKeyWithoutAFromEmail_IsNotConfigured_AndNamesTheSetting(bool isDevelopment)
    {
        var logger = new CapturingLogger<EmailService>();
        var transport = new FakeMailTransport();

        var result = await Service(Keyed(fromEmail: null), logger, transport, isDevelopment).SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.NotConfigured, result);
        Assert.Empty(transport.Sent);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("SendGrid:FromEmail", entry.Message);
        Assert.Contains(MaskedTo, entry.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(Text) || e.Message.Contains(ApiKey));
    }

    [Fact]
    public async Task SendAsync_WithCompleteSettingsAndNoTransport_IsNotConfigured_AndLogsAnError()
    {
        var logger = new CapturingLogger<EmailService>();

        var result = await Service(Keyed(), logger).SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.NotConfigured, result);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(MaskedTo, entry.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(Text));
    }

    [Theory]
    [InlineData(EmailSendResult.Sent)]
    [InlineData(EmailSendResult.Failed)]
    public async Task SendAsync_WithCompleteSettings_ReturnsTheTransportsResult(EmailSendResult transportResult)
    {
        var transport = new FakeMailTransport(transportResult);

        var result = await Service(Keyed(), new CapturingLogger<EmailService>(), transport).SendAsync(To, Subject, Text);

        Assert.Equal(transportResult, result);
        var sent = Assert.Single(transport.Sent);
        Assert.Equal((To, Subject, Text, (string?)null), (sent.To, sent.Subject, sent.Text, sent.Html));
    }

    [Fact]
    public async Task SendAsync_PassesTheHtmlPartToTheTransport()
    {
        var transport = new FakeMailTransport();

        await Service(Keyed(), new CapturingLogger<EmailService>(), transport).SendAsync(To, Subject, Text, "<p>Hello</p>");

        Assert.Equal("<p>Hello</p>", Assert.Single(transport.Sent).Html);
    }

    [Fact]
    public async Task SendAsync_WhenTheTransportThrows_IsFailed_AndLogsTheMaskedRecipient()
    {
        var logger = new CapturingLogger<EmailService>();
        var transport = new FakeMailTransport { ThrowOnSend = new HttpRequestException("The connection was reset.") };

        var result = await Service(Keyed(), logger, transport).SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Failed, result);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<HttpRequestException>(entry.Exception);
        Assert.DoesNotContain(To, entry.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(Text));
    }

    [Fact]
    public async Task SendAsync_WhenTheTransportTimesOutWithoutTheCallerCancelling_IsFailed()
    {
        var transport = new FakeMailTransport { ThrowOnSend = new TaskCanceledException("The request timed out.") };

        var result = await Service(Keyed(), new CapturingLogger<EmailService>(), transport).SendAsync(To, Subject, Text);

        Assert.Equal(EmailSendResult.Failed, result);
    }

    [Fact]
    public async Task SendAsync_WhenTheCallerCancels_RethrowsTheCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var transport = new CancellingTransport();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(Keyed(), new CapturingLogger<EmailService>(), transport).SendAsync(To, Subject, Text, ct: cts.Token));
    }

    [Fact]
    public async Task SendAsync_WithABlankRecipient_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service(Keyed(), new CapturingLogger<EmailService>()).SendAsync(" ", Subject, Text));
    }

    [Theory]
    [InlineData(EmailSendResult.Sent, true)]
    [InlineData(EmailSendResult.Logged, true)]
    [InlineData(EmailSendResult.NotConfigured, false)]
    [InlineData(EmailSendResult.Failed, false)]
    public void IsDelivered_IsTrueOnlyForSentAndLogged(EmailSendResult result, bool delivered)
    {
        Assert.Equal(delivered, result.IsDelivered());
    }

    /// <summary>Throws the cancellation it is given, as a transport does when the caller's token is cancelled.</summary>
    private sealed class CancellingTransport : IMailTransport
    {
        public Task<EmailSendResult> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default) =>
            Task.FromException<EmailSendResult>(new OperationCanceledException(ct));
    }
}
