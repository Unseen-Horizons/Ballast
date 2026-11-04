// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Auth.Tests;

public class AuthOptionsTests
{
    private static AuthOptions Read(IConfiguration configuration)
    {
        var options = new AuthOptions();
        options.ReadJwt(configuration.GetSection("Jwt"));
        return options;
    }

    [Fact]
    public void Defaults_are_the_values_get_there_api_has_shipped()
    {
        var options = Read(TestConfig.Build());

        Assert.Equal(15, options.AccessTokenMinutes);
        Assert.Equal(1, options.RefreshTokenDays);
        Assert.Equal(30, options.RefreshTokenDaysRememberMe);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ClockSkew);
        Assert.Equal("sub", options.Claims.Subject);
        Assert.Equal("email", options.Claims.Email);
        Assert.Equal("given_name", options.Claims.GivenName);
        Assert.Equal("role", options.Claims.Role);
        Assert.Equal("security_stamp", options.Claims.SecurityStamp);
        Assert.Equal("INVALID_CREDENTIALS", options.Failures.InvalidCredentials.Code);
        Assert.Equal(401, options.Failures.InvalidCredentials.Status);
        Assert.Equal("REFRESH_TOKEN_EXPIRED", options.Failures.RefreshTokenExpired.Code);
        Assert.Equal("INVALID_REFRESH_TOKEN", options.Failures.InvalidRefreshToken.Code);
        Assert.Equal(12, options.Password.RequiredLength);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.LockoutDuration);
        Assert.Equal(AuthPasswordSignInMode.All, options.PasswordSignIn);
        Assert.False(options.SecurityStampRequired);
        Assert.False(options.LegacyBcryptPasswords);
        options.Validate();
    }

    [Fact]
    public void An_absent_expiry_is_the_fifteen_minute_default()
    {
        Assert.Equal(15, Read(TestConfig.Build()).AccessTokenMinutes);
    }

    [Fact]
    public void A_blank_expiry_is_the_fifteen_minute_default()
    {
        var options = Read(TestConfig.Build(new KeyValuePair<string, string?>("Jwt:ExpiryMinutes", "   ")));

        Assert.Equal(15, options.AccessTokenMinutes);
    }

    [Fact]
    public void A_present_expiry_is_read_in_minutes()
    {
        var options = Read(TestConfig.Build(new KeyValuePair<string, string?>("Jwt:ExpiryMinutes", "45")));

        Assert.Equal(45, options.AccessTokenMinutes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1441")]
    [InlineData("soon")]
    public void A_present_expiry_that_is_not_a_number_of_minutes_in_range_stops_the_start(string value)
    {
        var configuration = TestConfig.Build(new KeyValuePair<string, string?>("Jwt:ExpiryMinutes", value));

        var error = Assert.Throws<InvalidOperationException>(() => Read(configuration));
        Assert.Contains("Jwt:ExpiryMinutes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refresh_lifetime_that_is_not_a_whole_number_stops_the_start()
    {
        var configuration = TestConfig.Build(new KeyValuePair<string, string?>("Jwt:RefreshTokenDays", "one"));

        var error = Assert.Throws<InvalidOperationException>(() => Read(configuration));
        Assert.Contains("RefreshTokenDays", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_key_stops_the_start()
    {
        var options = new AuthOptions { Issuer = "i", Audience = "a" };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void A_key_shorter_than_32_bytes_stops_the_start()
    {
        var options = new AuthOptions { Key = new string('k', 31), Issuer = "i", Audience = "a" };

        var error = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("32 bytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_placeholder_key_stops_the_start()
    {
        var options = new AuthOptions { Key = AuthOptions.PlaceholderKey, Issuer = "i", Audience = "a" };

        var error = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("placeholder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_of_32_bytes_is_accepted()
    {
        var options = new AuthOptions { Key = new string('k', 32), Issuer = "i", Audience = "a" };

        options.Validate();
    }

    [Fact]
    public void Remember_me_must_outlast_the_standard_refresh_lifetime()
    {
        var options = Read(TestConfig.Build(
            new KeyValuePair<string, string?>("Jwt:RefreshTokenDays", "7"),
            new KeyValuePair<string, string?>("Jwt:RefreshTokenDaysRememberMe", "7")));

        var error = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("RefreshTokenDaysRememberMe", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Owner_only_sign_in_needs_the_privileged_role_name()
    {
        var options = Read(TestConfig.Build());
        options.PasswordSignIn = AuthPasswordSignInMode.OwnerOnly;

        var error = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("PrivilegedRoleName", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_name_appears_in_the_messages()
    {
        var configuration = TestConfig.Build(new KeyValuePair<string, string?>("Jwt:Key", "short"));
        var options = Read(configuration);

        var error = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.StartsWith("Jwt:Key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBallastAuth_refuses_a_bad_key_before_anything_else_is_registered()
    {
        var services = new ServiceCollection();
        var configuration = TestConfig.Build(new KeyValuePair<string, string?>("Jwt:Key", "CHANGE-ME"));

        Assert.Throws<InvalidOperationException>(() => services.AddBallastAuth<StringUser, string, IdentityRole, Microsoft.EntityFrameworkCore.DbContext, StringRefresh>(configuration));
    }
}
