// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;

namespace Ballast.Email.Tests;

/// <summary>How the settings are read from a section, and which combinations are refused at startup.</summary>
public class MailSettingsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Fact]
    public void Read_ReadsTheKeysFromTheDefaultSection()
    {
        var config = Config(
            ("SendGrid:ApiKey", "SG.not-a-real-key"),
            ("SendGrid:FromEmail", "info@example.test"),
            ("SendGrid:FromName", "Example"),
            ("SendGrid:LogInsteadOfSending", "false"));

        var settings = MailSettings.Read(config);

        Assert.Equal("SendGrid", settings.SectionName);
        Assert.Equal("SG.not-a-real-key", settings.ApiKey);
        Assert.Equal("info@example.test", settings.FromEmail);
        Assert.Equal("Example", settings.FromName);
        Assert.False(settings.LogInsteadOfSending);
        Assert.True(settings.IsConfigured);
        Assert.Empty(settings.MissingSettings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_BlankValues_CountAsUnset(string blank)
    {
        var settings = MailSettings.Read(Config(("SendGrid:ApiKey", blank), ("SendGrid:FromEmail", blank), ("SendGrid:FromName", blank)));

        Assert.False(settings.IsConfigured);
        Assert.Null(settings.ApiKey);
        Assert.Null(settings.FromEmail);
        Assert.Null(settings.FromName);
    }

    [Fact]
    public void Read_NoKeyAndNoSender_IsValid_AndSendingIsOff()
    {
        var settings = MailSettings.Read(Config());

        Assert.False(settings.IsConfigured);
        Assert.Empty(settings.MissingSettings);
        Assert.Null(settings.LogInsteadOfSending);
    }

    [Fact]
    public void Read_UsesTheDefaultFromName_WhenTheSectionHasNone()
    {
        var settings = MailSettings.Read(Config(("SendGrid:FromEmail", "info@example.test")), defaultFromName: "Example app");

        Assert.Equal("Example app", settings.FromName);
    }

    [Fact]
    public void Read_KeyWithoutASender_Throws_NamingTheSetting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => MailSettings.Read(Config(("SendGrid:ApiKey", "SG.not-a-real-key"))));

        Assert.Contains("SendGrid:FromEmail", exception.Message);
        Assert.Contains("SendGrid:ApiKey", exception.Message);
    }

    [Fact]
    public void Read_CustomSection_NamesTheCustomSettingsInTheError()
    {
        var config = Config(("Mail:ApiKey", "SG.not-a-real-key"));

        var exception = Assert.Throws<InvalidOperationException>(() => MailSettings.Read(config, sectionName: "Mail"));

        Assert.Contains("Mail:FromEmail", exception.Message);
        Assert.DoesNotContain("SendGrid:", exception.Message);
    }

    [Fact]
    public void Read_ReadsTheCustomSection()
    {
        var config = Config(("Mail:ApiKey", "SG.not-a-real-key"), ("Mail:FromEmail", "info@example.test"));

        var settings = MailSettings.Read(config, sectionName: "Mail");

        Assert.Equal("Mail", settings.SectionName);
        Assert.Equal("info@example.test", settings.FromEmail);
        Assert.True(settings.IsConfigured);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData(" true ", true)]
    public void Read_LogSetting_AcceptsTrueAndFalse(string value, bool expected)
    {
        var settings = MailSettings.Read(Config(("SendGrid:LogInsteadOfSending", value)));

        Assert.Equal(expected, settings.LogInsteadOfSending);
    }

    [Fact]
    public void Read_LogSettingThatIsNotABoolean_Throws_NamingTheSetting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => MailSettings.Read(Config(("SendGrid:LogInsteadOfSending", "maybe"))));

        Assert.Contains("SendGrid:LogInsteadOfSending", exception.Message);
    }

    [Fact]
    public void MissingSettings_ListsTheSender_WhenTheKeyIsSet()
    {
        var settings = new MailSettings("SG.not-a-real-key", fromEmail: null, sectionName: "Mail");

        Assert.Equal(["Mail:FromEmail"], settings.MissingSettings);
    }

    [Fact]
    public void Constructor_TrimsValues()
    {
        var settings = new MailSettings("  SG.key  ", " info@example.test ", " Example ");

        Assert.Equal("SG.key", settings.ApiKey);
        Assert.Equal("info@example.test", settings.FromEmail);
        Assert.Equal("Example", settings.FromName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankSectionName_Throws(string sectionName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new MailSettings(null, null, sectionName: sectionName));
    }

    [Fact]
    public void Read_NullConfiguration_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MailSettings.Read(null!));
    }
}
