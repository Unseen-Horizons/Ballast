// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Email.Tests;

/// <summary>What the registration adds, and what it refuses at startup.</summary>
public class EmailServiceCollectionExtensionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static ServiceCollection Services(string environment)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environment));
        services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        return services;
    }

    [Fact]
    public async Task AddBallastEmail_WithoutAKey_RegistersNoTransport_AndLogsInDevelopment()
    {
        var services = Services("Development");
        services.AddBallastEmail(Config(("SendGrid:FromEmail", "info@example.test")));
        using var provider = services.BuildServiceProvider();

        var email = provider.GetRequiredService<EmailService>();

        Assert.Null(provider.GetService<IMailTransport>());
        Assert.Equal(EmailSendResult.Logged, await email.SendAsync("klijent@example.test", "Subject", "Text"));
    }

    [Fact]
    public void AddBallastEmail_WithoutAKey_OutsideDevelopment_SendsNothing()
    {
        var services = Services("Production");
        services.AddBallastEmail(Config());
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IMailTransport>());
        Assert.IsType<EmailService>(provider.GetRequiredService<EmailService>());
    }

    [Fact]
    public void AddBallastEmail_WithAKeyAndASender_RegistersTheSendGridTransport()
    {
        var services = Services("Production");
        services.AddBallastEmail(Config(("SendGrid:ApiKey", "SG.not-a-real-key"), ("SendGrid:FromEmail", "info@example.test")));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SendGridMailTransport>(provider.GetRequiredService<IMailTransport>());
    }

    [Fact]
    public void AddBallastEmail_WithAKeyButNoSender_Throws_BeforeTheAppStarts()
    {
        var services = Services("Production");

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddBallastEmail(Config(("SendGrid:ApiKey", "SG.not-a-real-key"))));

        Assert.Contains("SendGrid:FromEmail", exception.Message);
    }

    [Fact]
    public void AddBallastEmail_ReadsTheNamedSection()
    {
        var services = Services("Production");
        services.AddBallastEmail(
            Config(("Mail:ApiKey", "SG.not-a-real-key"), ("Mail:FromEmail", "info@example.test")),
            sectionName: "Mail",
            defaultFromName: "Example app");
        using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<MailSettings>();

        Assert.Equal("Mail", settings.SectionName);
        Assert.Equal("Example app", settings.FromName);
        Assert.IsType<SendGridMailTransport>(provider.GetRequiredService<IMailTransport>());
    }

    [Fact]
    public void AddBallastEmail_RunsTheClientConfiguration_WhenAKeyIsSet()
    {
        var services = Services("Production");
        var configured = false;

        services.AddBallastEmail(
            Config(("SendGrid:ApiKey", "SG.not-a-real-key"), ("SendGrid:FromEmail", "info@example.test")),
            configureHttpClient: _ => configured = true);

        Assert.True(configured);
    }

    [Fact]
    public void AddBallastEmail_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddBallastEmail(Config()));
    }
}
