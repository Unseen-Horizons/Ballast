// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AspNetCore.Tests.Support;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.AspNetCore.Tests.Security;

/// <summary>
/// The credential protector: the round trip, the passthrough of null and blank, a payload from another purpose that will not
/// open, both unreadable modes, the warning path of a read, and the registration helper.
/// </summary>
public class SecretProtectorTests
{
    private const string Secret = "bearer-abc-123";

    private static SecretProtector Make(
        IDataProtectionProvider provider,
        string purpose = "Tests.Credential",
        SecretUnreadableMode mode = SecretUnreadableMode.Throw,
        ILogger<SecretProtector>? logger = null) =>
        new(provider, logger ?? NullLogger<SecretProtector>.Instance, new SecretProtectorOptions { Purpose = purpose, UnreadableMode = mode });

    [Fact]
    public void A_credential_round_trips_and_is_not_stored_in_clear()
    {
        var protector = Make(new EphemeralDataProtectionProvider());

        var stored = protector.Protect(Secret);

        Assert.StartsWith("enc:v1:", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, stored);
        Assert.Equal(Secret, protector.Unprotect(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_and_blank_pass_through_unchanged(string? value)
    {
        var protector = Make(new EphemeralDataProtectionProvider());

        Assert.Equal(value, protector.Protect(value));
        Assert.Equal(value, protector.Unprotect(value));
        Assert.Equal(value, protector.UnprotectForRead(value, "source", out var warning));
        Assert.Null(warning);
    }

    [Fact]
    public void A_value_that_is_already_protected_is_not_wrapped_twice()
    {
        var protector = Make(new EphemeralDataProtectionProvider());
        var stored = protector.Protect(Secret);

        Assert.Equal(stored, protector.Protect(stored));
        Assert.Equal(Secret, protector.Unprotect(protector.Protect(stored)));
    }

    [Fact]
    public void A_value_without_the_marker_is_read_as_it_is()
    {
        var protector = Make(new EphemeralDataProtectionProvider());

        Assert.Equal("plain-text", protector.Unprotect("plain-text"));
    }

    [Fact]
    public void A_payload_from_another_purpose_does_not_open_and_throws_in_throw_mode()
    {
        var provider = new EphemeralDataProtectionProvider();
        var stored = Make(provider, purpose: "Owner.A").Protect(Secret);

        var reader = Make(provider, purpose: "Owner.B", mode: SecretUnreadableMode.Throw);

        Assert.Throws<SecretProtector.CredentialUnavailableException>(() => reader.Unprotect(stored));
    }

    [Fact]
    public void A_payload_from_another_purpose_reads_as_null_in_read_as_null_mode()
    {
        var provider = new EphemeralDataProtectionProvider();
        var stored = Make(provider, purpose: "Owner.A").Protect(Secret);

        var reader = Make(provider, purpose: "Owner.B", mode: SecretUnreadableMode.ReadAsNull);

        Assert.Null(reader.Unprotect(stored));
    }

    [Fact]
    public void The_correct_purpose_opens_a_payload_on_the_same_key_ring()
    {
        var provider = new EphemeralDataProtectionProvider();
        var stored = Make(provider, purpose: "Owner.A").Protect(Secret);

        Assert.Equal(Secret, Make(provider, purpose: "Owner.A").Unprotect(stored));
    }

    [Fact]
    public void A_read_with_no_credential_gives_no_warning()
    {
        var protector = Make(new EphemeralDataProtectionProvider());
        var stored = protector.Protect(Secret);

        var read = protector.UnprotectForRead(stored, "source", out var warning);

        Assert.Equal(Secret, read);
        Assert.Null(warning);
    }

    [Fact]
    public void An_unreadable_credential_is_read_as_no_credential_with_a_warning_that_names_the_source_not_the_value()
    {
        var provider = new EphemeralDataProtectionProvider();
        var stored = Make(provider, purpose: "Owner.A").Protect(Secret);
        var reader = Make(provider, purpose: "Owner.B");

        var read = reader.UnprotectForRead(stored, "Feed North", out var warning);

        Assert.Null(read);
        Assert.NotNull(warning);
        Assert.StartsWith("Feed North:", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, warning);
    }

    [Fact]
    public void The_unreadable_warning_is_logged_once_per_source_until_the_credential_reads_again()
    {
        var provider = new EphemeralDataProtectionProvider();
        var good = Make(provider, purpose: "Owner.A");
        var stored = good.Protect(Secret);
        var logs = new CapturedLogs();
        var reader = Make(provider, purpose: "Owner.B", mode: SecretUnreadableMode.ReadAsNull, logger: new LoggerFactory(new ILoggerProvider[] { logs }).CreateLogger<SecretProtector>());

        reader.UnprotectForRead(stored, "Feed North", out _);
        reader.UnprotectForRead(stored, "Feed North", out _);

        var warnings = logs.Records.Where(r => r.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("Feed North", warnings[0].Message);
        Assert.DoesNotContain(Secret, warnings[0].Message);

        // A credential that reads again clears the mark, so a later failure is logged again.
        reader.UnprotectForRead(null, "Feed North", out _);
        reader.UnprotectForRead(stored, "Feed North", out _);
        Assert.Equal(2, logs.Records.Count(r => r.Level == LogLevel.Warning));
    }

    [Fact]
    public void A_decrypt_failure_is_logged_as_an_error_without_the_value()
    {
        var provider = new EphemeralDataProtectionProvider();
        var stored = Make(provider, purpose: "Owner.A").Protect(Secret);
        var logs = new CapturedLogs();
        var reader = Make(provider, purpose: "Owner.B", logger: new LoggerFactory(new ILoggerProvider[] { logs }).CreateLogger<SecretProtector>());

        Assert.Throws<SecretProtector.CredentialUnavailableException>(() => reader.Unprotect(stored));

        var error = Assert.Single(logs.Records, r => r.Level == LogLevel.Error);
        Assert.DoesNotContain(Secret, error.Message);
    }

    [Fact]
    public void A_blank_purpose_or_an_undefined_mode_is_refused()
    {
        var provider = new EphemeralDataProtectionProvider();

        Assert.Throws<ArgumentException>(() => Make(provider, purpose: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(provider, mode: (SecretUnreadableMode)99));
        Assert.Throws<ArgumentNullException>(() => new SecretProtector(provider, NullLogger<SecretProtector>.Instance, null!));
    }

    [Fact]
    public void The_registration_helper_resolves_a_protector_with_its_purpose()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddLogging();
        services.AddSecretProtector("Owner.A");
        using var root = services.BuildServiceProvider();

        var protector = root.GetRequiredService<SecretProtector>();

        Assert.Same(protector, root.GetRequiredService<SecretProtector>());
        Assert.Equal(Secret, protector.Unprotect(protector.Protect(Secret)));
    }

    [Fact]
    public void The_registration_helper_keeps_the_purpose_and_the_mode()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddLogging();
        services.AddSecretProtector("Owner.A", SecretUnreadableMode.ReadAsNull);
        using var root = services.BuildServiceProvider();
        var stored = root.GetRequiredService<SecretProtector>().Protect(Secret);

        var other = new ServiceCollection();
        other.AddDataProtection();
        other.AddLogging();
        other.AddSecretProtector("Owner.B", SecretUnreadableMode.ReadAsNull);
        using var otherRoot = other.BuildServiceProvider();

        Assert.Null(otherRoot.GetRequiredService<SecretProtector>().Unprotect(stored));
    }

    [Fact]
    public void The_registration_helper_refuses_a_blank_purpose_or_an_undefined_mode()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddSecretProtector(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddSecretProtector("Owner.A", (SecretUnreadableMode)99));
    }
}
