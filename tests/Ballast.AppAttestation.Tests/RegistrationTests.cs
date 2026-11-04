// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

using Ballast.AppAttestation.Tests.Support;
using Ballast.AspNetCore;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation.Tests;

/// <summary>
/// The registration and its checks: a setup with no default for its app values, a stable set of codes, the option defaults, startup
/// refusals, the store a host keeps, and the wire names the request and answers use.
/// </summary>
public class RegistrationTests
{
    private static readonly string[] DefaultPlatforms = ["android", "ios", "maccatalyst", "windows"];

    // ASP.NET binds and writes bodies with these defaults (camel case, case-insensitive), so the test does the same.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static void CompleteSetup(AppAttestationSetup setup)
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
    }

    private static IConfiguration Configuration(IDictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?> { ["AppSession:Key"] = AppAttestationTestHost.SessionKey };
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
            settings[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    // ---- the setup has no default for an app value --------------------------------------------

    [Theory]
    [InlineData(nameof(AppAttestationSetup.PackageName))]
    [InlineData(nameof(AppAttestationSetup.Issuer))]
    [InlineData(nameof(AppAttestationSetup.Audience))]
    [InlineData(nameof(AppAttestationSetup.HeaderName))]
    [InlineData(nameof(AppAttestationSetup.CookieName))]
    [InlineData(nameof(AppAttestationSetup.RedirectPrefix))]
    [InlineData(nameof(AppAttestationSetup.SessionRequiredDetail))]
    [InlineData(nameof(AppAttestationSetup.SessionRequiredCode))]
    [InlineData(nameof(AppAttestationSetup.AttestationFailedCode))]
    [InlineData(nameof(AppAttestationSetup.AttestationUnavailableCode))]
    public void A_value_the_app_must_set_stops_the_registration_and_the_message_names_it(string property)
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() => services.AddAppAttestation(Configuration(), new HostingEnvironment(), setup =>
        {
            CompleteSetup(setup);
            typeof(AppAttestationSetup).GetProperty(property)!.SetValue(setup, string.Empty);
        }));

        Assert.Contains(property, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("view/")]
    [InlineData("/view")]
    [InlineData("/")]
    [InlineData("/vi ew/")]
    [InlineData("/vi\\ew/")]
    [InlineData("/vîew/")]
    public void A_redirect_prefix_must_start_and_end_with_a_slash_and_be_plain_ascii(string prefix)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAppAttestation(Configuration(), new HostingEnvironment(), setup =>
        {
            CompleteSetup(setup);
            setup.RedirectPrefix = prefix;
        }));

        Assert.Contains(nameof(AppAttestationSetup.RedirectPrefix), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad header")]
    [InlineData("bad:header")]
    public void A_header_name_with_a_space_or_a_colon_is_refused(string header)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAppAttestation(Configuration(), new HostingEnvironment(), setup =>
        {
            CompleteSetup(setup);
            setup.HeaderName = header;
        }));

        Assert.Contains(nameof(AppAttestationSetup.HeaderName), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad;cookie")]
    [InlineData("bad=cookie")]
    [InlineData("bad cookie")]
    public void A_cookie_name_with_a_separator_is_refused(string cookie)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAppAttestation(Configuration(), new HostingEnvironment(), setup =>
        {
            CompleteSetup(setup);
            setup.CookieName = cookie;
        }));

        Assert.Contains(nameof(AppAttestationSetup.CookieName), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_platform_name_that_is_not_lower_case_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAppAttestation(Configuration(), new HostingEnvironment(), setup =>
        {
            CompleteSetup(setup);
            setup.Platforms = ["Android"];
        }));

        Assert.Contains(nameof(AppAttestationSetup.Platforms), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_setup_names_the_package_first()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAppAttestation(Configuration(), new HostingEnvironment(), _ => { }));

        Assert.Contains(nameof(AppAttestationSetup.PackageName), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_section_names_are_the_ones_the_configuration_has_always_used()
    {
        var setup = new AppAttestationSetup();

        Assert.Equal("AppSession", setup.SessionSection);
        Assert.Equal("AppAttestation", setup.AttestationSection);
        Assert.Equal("PlayIntegrity", setup.PlayIntegritySection);
        Assert.Equal("App verification required", setup.SessionRequiredTitle);
        Assert.Equal(DefaultPlatforms, setup.Platforms);
    }

    [Fact]
    public void A_setup_naming_its_own_section_reads_the_key_from_that_section()
    {
        // The key is under "Sample", not "AppSession": with the default section the start is refused, with the named one it is not.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sample:Key"] = AppAttestationTestHost.SessionKey })
            .Build();

        var refused = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAppAttestation(configuration, new HostingEnvironment(), CompleteSetup));
        Assert.Contains("AppSession:Key", refused.Message, StringComparison.Ordinal);

        var services = new ServiceCollection();
        services.AddAppAttestation(configuration, new HostingEnvironment(), setup =>
        {
            CompleteSetup(setup);
            setup.SessionSection = "Sample";
        });
        Assert.NotNull(services.BuildServiceProvider().GetRequiredService<AppSessionValidator>());
    }

    // ---- the codes are a stable wire --------------------------------------------------------

    [Fact]
    public void The_library_codes_are_the_stable_wire_values()
    {
        Assert.Equal("APP_SESSION_REQUIRED", AppAttestationCodes.SessionRequired);
        Assert.Equal("APP_ATTESTATION_FAILED", AppAttestationCodes.AttestationFailed);
        Assert.Equal("APP_ATTESTATION_UNAVAILABLE", AppAttestationCodes.AttestationUnavailable);
    }

    [Fact]
    public void The_attestation_values_are_the_three_the_token_carries()
    {
        Assert.Equal("play", Attestation.Play);
        Assert.Equal("dev", Attestation.Developer);
        Assert.Equal("none", Attestation.None);
        Assert.True(Attestation.IsKnown("play"));
        Assert.False(Attestation.IsKnown("PLAY"));
        Assert.False(Attestation.IsKnown(null));
    }

    [Fact]
    public void The_token_and_scheme_names_are_the_ones_installed_clients_and_tokens_already_carry()
    {
        Assert.Equal("AppSession", AppAttestationDefaults.SchemeName);
        Assert.Equal("sid", AppAttestationDefaults.SessionIdClaim);
        Assert.Equal("att", AppAttestationDefaults.AttestationClaim);
        Assert.Equal("plat", AppAttestationDefaults.PlatformClaim);
    }

    // ---- defaults ------------------------------------------------------------------------------

    [Fact]
    public void The_option_defaults_are_the_documented_values()
    {
        var session = new AppSessionOptions();
        Assert.Equal(24, session.LifetimeHours);
        Assert.Equal(TimeSpan.FromHours(24), session.Lifetime);
        Assert.Equal(TimeSpan.FromMinutes(5), session.NonceLifetime);
        Assert.Equal(TimeSpan.FromSeconds(60), session.TicketLifetime);
        Assert.Equal(20_000, session.StoreCapacity);

        var attestation = new AppAttestationOptions();
        Assert.False(attestation.Enforce);
        Assert.False(attestation.AllowUnattested);
        Assert.Empty(attestation.DeveloperCertificates);

        Assert.Equal("Ballast.AppAttestation", new PlayIntegrityOptions().ApplicationName);
        Assert.Equal(string.Empty, new PlayIntegrityOptions().ServiceAccountJson);
    }

    [Theory]
    [InlineData("AppSession:LifetimeHours", "0")]
    [InlineData("AppSession:LifetimeHours", "169")]
    [InlineData("AppSession:StoreCapacity", "0")]
    public void A_lifetime_or_capacity_outside_its_range_stops_the_start_and_is_not_silently_replaced(string key, string value)
    {
        var services = new ServiceCollection();
        services.AddAppAttestation(Configuration(new Dictionary<string, string?> { [key] = value }), new HostingEnvironment(), CompleteSetup);
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AppSessionOptions>>().Value);
        Assert.Contains(key.Split(':')[1], string.Join(' ', error.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lifetime_of_one_week_is_the_longest_allowed()
    {
        var services = new ServiceCollection();
        services.AddAppAttestation(Configuration(new Dictionary<string, string?> { ["AppSession:LifetimeHours"] = "168" }), new HostingEnvironment(), CompleteSetup);

        Assert.Equal(168, services.BuildServiceProvider().GetRequiredService<IOptions<AppSessionOptions>>().Value.LifetimeHours);
    }

    // ---- the store a host keeps ----------------------------------------------------------------

    [Fact]
    public async Task A_nonce_store_the_app_registers_first_is_the_one_the_bootstrap_uses()
    {
        var store = new CountingNonceStore(new InMemoryAppSessionNonceStore(TimeProvider.System, 100));

        await using var host = await AppAttestationTestHost.StartAsync(beforeAppAttestation: services => services.AddSingleton<IAppSessionNonceStore>(store));

        var response = await host.Client.GetAsync("/app/session/nonce");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, store.Adds);
        Assert.Same(store, host.Services.GetRequiredService<IAppSessionNonceStore>());
    }

    [Fact]
    public async Task Without_an_app_store_the_in_memory_store_is_the_default()
    {
        await using var host = await AppAttestationTestHost.StartAsync();

        Assert.IsType<InMemoryAppSessionNonceStore>(host.Services.GetRequiredService<IAppSessionNonceStore>());
        Assert.IsType<InMemoryAppSessionTicketStore>(host.Services.GetRequiredService<IAppSessionTicketStore>());
    }

    // ---- start-up messages ---------------------------------------------------------------------

    [Fact]
    public async Task A_developer_certificate_that_is_not_a_digest_is_logged_at_startup()
    {
        await using var host = await AppAttestationTestHost.StartAsync(developerCertificates: ["not a digest"]);

        Assert.Contains(host.Logs.Lines, l => l.StartsWith("Error", StringComparison.Ordinal) && l.Contains("not a SHA-256 digest") && l.Contains("not a digest"));
    }

    [Fact]
    public async Task Production_without_a_service_account_warns_at_startup_and_names_the_setting()
    {
        await using var host = await AppAttestationTestHost.StartAsync(environment: "Production");

        Assert.Contains(host.Logs.Lines, l => l.StartsWith("Warning", StringComparison.Ordinal) && l.Contains("PlayIntegrity:ServiceAccountJson"));
    }

    [Fact]
    public async Task The_startup_line_says_which_gate_mode_is_on()
    {
        await using var logOnly = await AppAttestationTestHost.StartAsync(enforce: false);
        Assert.Contains(logOnly.Logs.Lines, l => l.Contains("log-only") && l.Contains("Developer certificates configured: 0"));

        await using var enforced = await AppAttestationTestHost.StartAsync(enforce: true);
        Assert.Contains(enforced.Logs.Lines, l => l.Contains("ENFORCED"));
    }

    // ---- wire names ----------------------------------------------------------------------------

    [Fact]
    public void The_request_and_answers_keep_their_json_names()
    {
        var request = JsonSerializer.Deserialize<CreateAppSessionRequest>(
            """{"platform":"android","installId":"install-0000000001","nonce":"n","integrityToken":"t"}""",
            WebOptions);
        Assert.NotNull(request);
        Assert.Equal("android", request.Platform);
        Assert.Equal("install-0000000001", request.InstallId);
        Assert.Equal("n", request.Nonce);
        Assert.Equal("t", request.IntegrityToken);

        var session = JsonSerializer.Serialize(new AppSessionResponse { AccessToken = "x", ExpiresInSeconds = 60 }, WebOptions);
        Assert.Contains("\"accessToken\":\"x\"", session, StringComparison.Ordinal);
        Assert.Contains("\"expiresInSeconds\":60", session, StringComparison.Ordinal);

        Assert.Contains("\"nonce\":\"abc\"", JsonSerializer.Serialize(new AppNonceResponse { Nonce = "abc" }, WebOptions), StringComparison.Ordinal);
        Assert.Contains("\"ticket\":\"abc\"", JsonSerializer.Serialize(new AppWebTicketResponse { Ticket = "abc" }, WebOptions), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rate_limit_policy_partitions_by_session_and_takes_the_configuration_key_it_is_given()
    {
        var options = new RateLimitOptions();
        options.AddAppAttestationPolicy("Sample", 5, "SampleLimit");

        var policy = Assert.Single(options.Policies);
        Assert.Equal("Sample", policy.Name);
        Assert.Equal(5, policy.DefaultPermitLimit);
        Assert.Equal("SampleLimit", policy.ConfigurationKey);
        Assert.NotNull(policy.PartitionKey);
    }

    [Fact]
    public void A_policy_with_a_blank_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new RateLimitOptions().AddAppAttestationPolicy(" ", 5));
    }

    /// <summary>A nonce store that counts the nonces it is given, around the in-memory one.</summary>
    private sealed class CountingNonceStore(IAppSessionNonceStore inner) : IAppSessionNonceStore
    {
        public int Adds { get; private set; }

        public ValueTask<bool> AddAsync(string nonce, TimeSpan lifetime, CancellationToken ct = default)
        {
            Adds++;
            return inner.AddAsync(nonce, lifetime, ct);
        }

        public ValueTask<bool> TakeAsync(string nonce, CancellationToken ct = default) => inner.TakeAsync(nonce, ct);
    }
}
