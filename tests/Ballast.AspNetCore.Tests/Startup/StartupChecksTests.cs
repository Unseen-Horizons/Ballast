// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;

namespace Ballast.AspNetCore.Tests.Startup;

/// <summary>The secret check: a missing, blank, placeholder or short secret stops the start, and the message names the key and never the value.</summary>
public class StartupChecksTests
{
    private const string Key = "Jwt:Key";

    [Fact]
    public void A_valid_secret_is_returned_as_configured()
    {
        var secret = new string('k', 32);

        Assert.Equal(secret, StartupChecks.RequireSecret(Config(secret), Key, 32, "CHANGE-ME"));
    }

    [Fact]
    public void A_missing_secret_stops_the_start_and_the_message_says_how_to_set_it()
    {
        var error = Assert.Throws<InvalidOperationException>(() => StartupChecks.RequireSecret(new ConfigurationBuilder().Build(), Key, 32, "CHANGE-ME"));

        Assert.Contains("Jwt:Key must be configured", error.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set \"Jwt:Key\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("Jwt__Key", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_secret_stops_the_start(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => StartupChecks.RequireSecret(Config(value), Key, 32));

        Assert.Contains("Jwt:Key must be configured", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CHANGE-ME")]
    [InlineData("change-me")]
    public void A_placeholder_stops_the_start_whatever_its_case(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => StartupChecks.RequireSecret(Config(value), Key, 4, "CHANGE-ME"));

        Assert.Contains("placeholder", error.Message, StringComparison.Ordinal);
        Assert.Contains("Jwt:Key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_shorter_than_the_minimum_stops_the_start_and_the_message_does_not_show_it()
    {
        const string value = "hunter2";

        var error = Assert.Throws<InvalidOperationException>(() => StartupChecks.RequireSecret(Config(value), Key, 32));

        Assert.Contains("at least 32 bytes", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_length_is_counted_in_utf8_bytes_not_characters()
    {
        // Five characters, each two bytes in UTF-8: ten bytes in all.
        const string value = "čćšđž";

        Assert.Equal(value, StartupChecks.RequireSecret(Config(value), Key, 10));
        Assert.Throws<InvalidOperationException>(() => StartupChecks.RequireSecret(Config(value), Key, 11));
    }

    [Fact]
    public void A_minimum_below_one_byte_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() => StartupChecks.RequireSecret(Config("x"), Key, 0));
    }

    [Fact]
    public void The_environment_variable_name_for_a_nested_key_uses_double_underscores()
    {
        var error = Assert.Throws<InvalidOperationException>(() => StartupChecks.RequireSecret(new ConfigurationBuilder().Build(), "AppSession:Key", 32));

        Assert.Contains("AppSession__Key", error.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Config(string value) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [Key] = value }).Build();
}
