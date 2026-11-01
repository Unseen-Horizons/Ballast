// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Health.Tests;

/// <summary>
/// The registration rules: what each setup value changes, what is refused (a second call, a duplicate name, a negative cap, a
/// blank name), that an uncapped gate is never Degraded, that an outside check takes part in the hold and the log, and that the
/// capped gates answer as the readiness rules say.
/// </summary>
public class RegistrationTests
{
    private static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build();

    /// <summary>An outside check that is always Healthy.</summary>
    private sealed class HealthyOutsideCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy("Outside check."));
    }

    /// <summary>An outside check that is always Unhealthy.</summary>
    private sealed class UnhealthyOutsideCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Unhealthy("Outside check."));
    }

    [Fact]
    public void The_defaults_are_the_GetThere_defaults()
    {
        var setup = new HealthSetupOptions();
        var options = new HealthOptions();

        Assert.Equal("Health", setup.SettingsSection);
        Assert.Equal("BuiltAtUtc", setup.BuiltAtMetadataKey);
        Assert.Equal("Ballast.Health", setup.LogCategory);
        Assert.Null(setup.BuildAssembly);
        Assert.Equal(15, options.DatabaseCacheSeconds);
        Assert.Equal(90, options.DatabaseGraceSeconds);
        Assert.Equal(5, options.SchemaRecheckSeconds);
        Assert.Equal(5, options.CheckTimeoutSeconds);
        Assert.Equal(0, options.StartupHoldMaxSeconds);
        Assert.Equal("ready", ReadinessEndpoint.ReadyTag);
    }

    [Fact]
    public void The_settings_bind_from_the_section_the_setup_names_and_not_from_the_default()
    {
        var services = new ServiceCollection();
        services.AddBallastHealth(Settings(("Probe:DatabaseGraceSeconds", "42"), ("Health:DatabaseGraceSeconds", "7")), o => o.SettingsSection = "Probe");

        using var provider = services.BuildServiceProvider();
        Assert.Equal(42, provider.GetRequiredService<IOptions<HealthOptions>>().Value.DatabaseGraceSeconds);
    }

    [Fact]
    public void Without_a_section_name_the_settings_bind_from_Health()
    {
        var services = new ServiceCollection();
        services.AddBallastHealth(Settings(("Health:DatabaseGraceSeconds", "7")));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(7, provider.GetRequiredService<IOptions<HealthOptions>>().Value.DatabaseGraceSeconds);
    }

    [Fact]
    public void The_build_time_is_read_from_the_assembly_and_key_the_setup_names()
    {
        var services = new ServiceCollection();
        services.AddBallastHealth(Settings(), o =>
        {
            o.BuildAssembly = typeof(BuildInfoTests).Assembly;
            o.BuiltAtMetadataKey = "Ballast.Health.TestBuiltAt";
        });

        using var provider = services.BuildServiceProvider();
        Assert.Equal("2026-10-21T10:00:00Z", provider.GetRequiredService<BuildInfo>().BuiltAtUtc);
    }

    [Theory]
    [InlineData("SettingsSection")]
    [InlineData("BuiltAtMetadataKey")]
    [InlineData("LogCategory")]
    public void A_blank_setup_value_is_refused(string which)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddBallastHealth(Settings(), o =>
        {
            switch (which)
            {
                case "SettingsSection": o.SettingsSection = " "; break;
                case "BuiltAtMetadataKey": o.BuiltAtMetadataKey = string.Empty; break;
                default: o.LogCategory = "  "; break;
            }
        }));
    }

    [Fact]
    public void Calling_AddBallastHealth_twice_is_refused()
    {
        var services = new ServiceCollection();
        services.AddBallastHealth(Settings());

        Assert.Throws<InvalidOperationException>(() => services.AddBallastHealth(Settings()));
    }

    [Fact]
    public void A_second_database_check_is_refused()
    {
        var services = new ServiceCollection();
        services.AddBallastHealth(Settings()).AddDatabaseReadiness<MigrationProbeContext>();

        Assert.Throws<InvalidOperationException>(() => services.AddDatabaseReadiness<MigrationProbeContext>("second"));
    }

    [Fact]
    public void A_second_schema_check_is_refused()
    {
        var services = new ServiceCollection();
        services.AddBallastHealth(Settings()).AddSchemaReadiness<MigrationProbeContext>();

        Assert.Throws<InvalidOperationException>(() => services.AddSchemaReadiness<MigrationProbeContext>("second"));
    }

    [Fact]
    public void A_duplicate_gate_name_is_refused()
    {
        var services = new ServiceCollection();
        services.AddBallastHealth(Settings()).AddStartupGate("gate", _ => true, TimeSpan.FromSeconds(10));

        Assert.Throws<InvalidOperationException>(() => services.AddStartupGate("gate", _ => true, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Two_outside_checks_with_one_name_are_refused_when_the_checks_run()
    {
        // The health service, not this library, refuses a duplicate name: it does so when it first builds its checks.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBallastHealth(Settings())
            .AddReadinessCheck<HealthyOutsideCheck>("same")
            .AddReadinessCheck<UnhealthyOutsideCheck>("same");

        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.GetRequiredService<HealthCheckService>().CheckHealthAsync());
    }

    [Fact]
    public void A_negative_cap_is_refused()
    {
        var services = new ServiceCollection();
        services.AddBallastHealth(Settings());

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddStartupGate("gate", _ => true, TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddWarmupGate(TimeSpan.FromSeconds(-1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_gate_name_is_refused(string name)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddStartupGate(name, _ => true, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => services.AddUncappedStartupGate(name, _ => true));
    }

    [Fact]
    public async Task An_uncapped_gate_is_never_degraded_however_long_it_waits()
    {
        var time = new ManualTime();
        var logger = new CapturingLogger();
        var start = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        var gate = new StartupGateHealthCheck("seed", () => false, TimeSpan.MaxValue, start, time, new ReadinessLog(logger, time, new HealthStart(start)));

        time.Advance(TimeSpan.FromDays(36500));

        var result = await gate.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task An_uncapped_gate_turns_healthy_once_its_fact_holds()
    {
        var on = false;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBallastHealth(Settings()).AddUncappedStartupGate("seed", _ => on);

        using var provider = services.BuildServiceProvider();
        var check = provider.GetRequiredKeyedService<IHealthCheck>("seed");
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);

        on = true;
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Fact]
    public async Task A_capped_gate_past_its_cap_is_degraded_on_the_route_and_warns_once_in_the_log()
    {
        var provider = new CapturingLoggerProvider();

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Logging.AddProvider(provider);
                builder.Services.AddBallastHealth(builder.Configuration);
                builder.Services.AddStartupGate("slow", _ => false, TimeSpan.Zero);
            },
            app => app.MapReadiness(),
            async (_, client) =>
            {
                var first = await client.GetAsync("/health/ready");
                var second = await client.GetAsync("/health/ready");

                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                Assert.Equal("{\"status\":\"Degraded\",\"checks\":{\"slow\":\"Degraded\"}}", await first.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.OK, second.StatusCode);

                // Once, however many probes come in. The seconds depend on how long the test host took to start, so only the shape is fixed.
                var warning = Assert.Single(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Startup gate slow was not met after ", StringComparison.Ordinal));
                Assert.EndsWith(" s; reporting ready anyway", warning.Message, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task A_capped_gate_whose_fact_holds_is_healthy_and_names_the_fact()
    {
        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddBallastHealth(builder.Configuration);
                builder.Services.AddStartupGate("fact", _ => true, TimeSpan.FromMinutes(5));
            },
            app => app.MapReadiness(),
            async (_, client) =>
            {
                var response = await client.GetAsync("/health/ready");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("{\"status\":\"Healthy\",\"checks\":{\"fact\":\"Healthy\"}}", await response.Content.ReadAsStringAsync());
            });
    }

    [Fact]
    public async Task The_readiness_log_lines_go_under_the_category_the_setup_names()
    {
        var provider = new CapturingLoggerProvider();

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Logging.AddProvider(provider);
                builder.Services.AddBallastHealth(builder.Configuration, o => o.LogCategory = "Shop.Readiness");
                builder.Services.AddReadinessCheck<HealthyOutsideCheck>("external");
            },
            app => app.MapReadiness(),
            async (_, client) =>
            {
                await client.GetAsync("/health/ready");
            });

        Assert.Contains(provider.Entries, e => e.Category == "Shop.Readiness" && e.Message.StartsWith("Ready after ", StringComparison.Ordinal));
        Assert.Contains(provider.Entries, e => e.Category == "Shop.Readiness" && e.Message.StartsWith("Readiness probe: user agent", StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Entries, e => e.Category == "Ballast.Health");
    }

    [Fact]
    public async Task An_outside_check_that_is_unhealthy_holds_the_port_to_its_cap_and_is_named_in_the_log()
    {
        var provider = new CapturingLoggerProvider();

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Logging.AddProvider(provider);
                builder.Configuration["Health:StartupHoldMaxSeconds"] = "1";
                builder.Services.AddBallastHealth(builder.Configuration);
                builder.Services.AddReadinessCheck<UnhealthyOutsideCheck>("external");
                builder.Services.AddStartupHold();
            },
            app => app.MapReadiness(),
            async (_, client) =>
            {
                // The hold returned at its cap, while the outside check was still Unhealthy. Without the check's tag the hold
                // would have seen no Unhealthy check and opened at once, with no such warning.
                Assert.Contains(provider.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("port opens at the 1 s cap while readiness is Unhealthy", StringComparison.Ordinal));

                var response = await client.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("{\"status\":\"Unhealthy\",\"checks\":{\"external\":\"Unhealthy\"}}", await response.Content.ReadAsStringAsync());
                Assert.Contains(provider.Entries, e => e.Message == "Not ready, waiting on: external");
            });
    }

    [Fact]
    public async Task An_outside_check_that_is_healthy_lets_the_hold_open_the_port_at_once()
    {
        var provider = new CapturingLoggerProvider();

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Logging.AddProvider(provider);
                builder.Configuration["Health:StartupHoldMaxSeconds"] = "60";
                builder.Services.AddBallastHealth(builder.Configuration);
                builder.Services.AddReadinessCheck<HealthyOutsideCheck>("external");
                builder.Services.AddStartupHold();
            },
            app => app.MapReadiness(),
            (_, _) =>
            {
                // The seconds depend on how long the host took to start, so only the shape of the line is fixed.
                Assert.Contains(provider.Entries, e => e.Message.StartsWith("Startup hold: port opens after ", StringComparison.Ordinal) && e.Message.EndsWith(", readiness Healthy", StringComparison.Ordinal));
                Assert.DoesNotContain(provider.Entries, e => e.Level == LogLevel.Warning);
                return Task.CompletedTask;
            });
    }
}
