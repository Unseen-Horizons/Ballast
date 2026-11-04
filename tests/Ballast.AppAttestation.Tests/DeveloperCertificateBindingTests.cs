// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AppAttestation.Tests.Support;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation.Tests;

/// <summary>
/// How <c>DeveloperCertificates</c> reaches the options from configuration. App Service settings are flat: one comma-separated value is
/// how the owner sets it, and the binder alone ignores that for a <c>string[]</c> (production showed "Developer certificates configured:
/// 0", 2026-10-05).
/// </summary>
public class DeveloperCertificateBindingTests
{
    private const string DebugHex = "B5:AD:BB:F6:13:F2:4B:C4:D7:8E:BD:B8:C4:CC:E1:E7:6D:6B:3C:DB:80:23:AB:AE:15:E6:0F:6D:B5:63:21:9B";
    private const string ReleaseHex = "E2:06:40:CF:DB:38:AE:CB:B8:2C:0F:2B:0A:8C:A0:20:78:34:6A:DD:00:CE:40:DC:F0:A7:74:A8:3C:7F:39:DF";

    private static AppAttestationOptions Bind(Dictionary<string, string?> settings)
    {
        settings["AppSession:Key"] = new string('k', 48);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddAppAttestation(configuration, new HostingEnvironment { EnvironmentName = "Production" }, setup =>
        {
            setup.PackageName = AppAttestationTestHost.Package;
            setup.Issuer = AppAttestationTestHost.Issuer;
            setup.Audience = AppAttestationTestHost.Audience;
            setup.HeaderName = AppAttestationTestHost.Header;
            setup.CookieName = AppAttestationTestHost.Cookie;
            setup.RedirectPrefix = AppAttestationTestHost.Prefix;
            setup.SessionRequiredDetail = "detail";
            setup.SessionRequiredCode = AppAttestationCodes.SessionRequired;
            setup.AttestationFailedCode = AppAttestationCodes.AttestationFailed;
            setup.AttestationUnavailableCode = AppAttestationCodes.AttestationUnavailable;
        });
        return services.BuildServiceProvider().GetRequiredService<IOptions<AppAttestationOptions>>().Value;
    }

    [Fact]
    public void One_comma_separated_setting_yields_both_certificates()
    {
        var options = Bind(new() { ["AppAttestation:DeveloperCertificates"] = $"{DebugHex},{ReleaseHex}" });

        var digests = options.NormalizedDeveloperDigests(out var rejected);

        Assert.Equal(2, digests.Count);
        Assert.Empty(rejected);
        Assert.Contains("ta279hPyS8TXjr24xMzh521rPNuAI6uuFeYPbbVjIZs", digests); // the debug key as Play Integrity spells it
    }

    [Fact]
    public void Numbered_settings_still_bind()
    {
        var options = Bind(new()
        {
            ["AppAttestation:DeveloperCertificates:0"] = DebugHex,
            ["AppAttestation:DeveloperCertificates:1"] = ReleaseHex,
        });

        Assert.Equal(2, options.NormalizedDeveloperDigests(out _).Count);
    }

    [Fact]
    public void No_setting_means_no_certificates()
    {
        Assert.Empty(Bind([]).NormalizedDeveloperDigests(out _));
    }

    [Fact]
    public void The_section_the_setup_names_is_the_one_read()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSession:Key"] = new string('k', 48),
                ["Sample:DeveloperCertificates"] = DebugHex,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddAppAttestation(configuration, new HostingEnvironment { EnvironmentName = "Production" }, setup =>
        {
            setup.AttestationSection = "Sample";
            setup.PackageName = AppAttestationTestHost.Package;
            setup.Issuer = AppAttestationTestHost.Issuer;
            setup.Audience = AppAttestationTestHost.Audience;
            setup.HeaderName = AppAttestationTestHost.Header;
            setup.CookieName = AppAttestationTestHost.Cookie;
            setup.RedirectPrefix = AppAttestationTestHost.Prefix;
            setup.SessionRequiredDetail = "detail";
            setup.SessionRequiredCode = AppAttestationCodes.SessionRequired;
            setup.AttestationFailedCode = AppAttestationCodes.AttestationFailed;
            setup.AttestationUnavailableCode = AppAttestationCodes.AttestationUnavailable;
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<AppAttestationOptions>>().Value;

        Assert.Single(options.NormalizedDeveloperDigests(out _));
    }
}
