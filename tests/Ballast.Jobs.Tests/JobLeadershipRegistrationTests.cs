// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Health;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Jobs.Tests;

/// <summary>What <c>AddJobLeadership</c>, <c>ConfigureGracefulShutdown</c>, <c>AddLeaderHostedService</c> and <c>AddJobLockReadiness</c> put in the container.</summary>
public class JobLeadershipRegistrationTests
{
    /// <summary>A worker with nothing to do, for the registration tests.</summary>
    private sealed class RegistrationProbe : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) => Settings.From(values);

    [Fact]
    public void With_the_lock_off_the_always_holder_is_registered_and_no_hosted_service_is_added()
    {
        var services = new ServiceCollection();

        services.AddJobLeadership(Config(), "Test:jobs", "Server=unused");

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IJobLeadership));
        Assert.Equal(typeof(AlwaysHolderLeadership), descriptor.ImplementationType);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void With_the_lock_on_the_leadership_and_the_hosted_service_are_one_instance()
    {
        using var lifetime = new StubLifetime();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        services.AddJobLeadership(Config(("JobLeadership:Enabled", "true")), "Test:jobs", "Server=unused");

        using var provider = services.BuildServiceProvider();
        var leadership = provider.GetRequiredService<IJobLeadership>();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        Assert.IsType<JobLeadership>(leadership);
        Assert.Same(leadership, hosted);
        Assert.True(leadership.Enabled);
        Assert.False(leadership.IsHolder);
        Assert.Equal(JobLeadershipOutcome.NotStarted, leadership.LastOutcome);
    }

    [Fact]
    public void The_settings_are_read_from_the_section_the_setup_names()
    {
        using var lifetime = new StubLifetime();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        services.AddJobLeadership(
            Config(("Lock:Enabled", "true")),
            "Test:jobs",
            "Server=unused",
            setup => setup.SettingsSection = "Lock");

        using var provider = services.BuildServiceProvider();
        Assert.IsType<JobLeadership>(provider.GetRequiredService<IJobLeadership>());
    }

    [Fact]
    public void The_lock_resource_is_required()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddJobLeadership(Config(), " ", "Server=unused"));
    }

    [Fact]
    public void The_connection_string_is_required()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddJobLeadership(Config(), "Test:jobs", " "));
    }

    [Fact]
    public void A_blank_settings_section_is_refused()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddJobLeadership(
            Config(), "Test:jobs", "Server=unused", setup => setup.SettingsSection = " "));
    }

    [Fact]
    public void The_words_of_the_409_answer_come_from_the_setup()
    {
        var services = new ServiceCollection();

        services.AddJobLeadership(
            Config(),
            "Test:jobs",
            "Server=unused",
            setup =>
            {
                setup.RequireJobHolder.Title = "Busy here.";
                setup.RequireJobHolder.Detail = "Wait a moment.";
            });

        using var provider = services.BuildServiceProvider();
        var words = provider.GetRequiredService<RequireJobHolderOptions>();
        Assert.Equal("Busy here.", words.Title);
        Assert.Equal("Wait a moment.", words.Detail);
    }

    [Fact]
    public void The_409_words_have_the_english_defaults_when_the_setup_does_not_change_them()
    {
        var services = new ServiceCollection();

        services.AddJobLeadership(Config(), "Test:jobs", "Server=unused");

        using var provider = services.BuildServiceProvider();
        var words = provider.GetRequiredService<RequireJobHolderOptions>();
        Assert.Equal(RequireJobHolderOptions.DefaultTitle, words.Title);
        Assert.Equal(RequireJobHolderOptions.DefaultDetail, words.Detail);
    }

    [Fact]
    public void The_graceful_shutdown_wait_defaults_to_sixty_seconds()
    {
        var services = new ServiceCollection();

        services.ConfigureGracefulShutdown(Config());

        using var provider = services.BuildServiceProvider();
        Assert.Equal(TimeSpan.FromSeconds(60), provider.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
    }

    [Fact]
    public void The_graceful_shutdown_default_can_be_given_by_the_app()
    {
        var services = new ServiceCollection();

        services.ConfigureGracefulShutdown(Config(), defaultSeconds: 30);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(TimeSpan.FromSeconds(30), provider.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
    }

    [Fact]
    public void The_graceful_shutdown_wait_is_read_from_configuration()
    {
        var services = new ServiceCollection();

        services.ConfigureGracefulShutdown(Config(("Hosting:ShutdownTimeoutSeconds", "90")));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(TimeSpan.FromSeconds(90), provider.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
    }

    [Fact]
    public void A_zero_shutdown_wait_is_not_taken_literally()
    {
        var services = new ServiceCollection();

        services.ConfigureGracefulShutdown(Config(("Hosting:ShutdownTimeoutSeconds", "0")));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(TimeSpan.FromSeconds(1), provider.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
    }

    [Fact]
    public void A_leader_service_registers_the_worker_as_a_singleton_and_runs_it_behind_the_wrapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJobLeadership>(new AlwaysHolderLeadership());

        services.AddLeaderHostedService<RegistrationProbe>();

        using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var wrapper = Assert.IsType<LeaderHostedService<RegistrationProbe>>(hosted);
        Assert.Equal(LeaderHostedService<RegistrationProbe>.DefaultStopBudget, wrapper.StopBudget);
        Assert.Same(provider.GetRequiredService<RegistrationProbe>(), provider.GetRequiredService<RegistrationProbe>());
    }

    [Fact]
    public void A_leader_service_can_be_given_the_instance_something_else_already_registers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJobLeadership>(new AlwaysHolderLeadership());
        services.AddSingleton<RegistrationProbe>();

        services.AddLeaderHostedService(sp => sp.GetRequiredService<RegistrationProbe>());

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IHostedService>());
        Assert.Single(services, d => d.ServiceType == typeof(RegistrationProbe));
    }

    [Fact]
    public void A_leader_service_takes_the_stop_budget_it_is_given()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJobLeadership>(new AlwaysHolderLeadership());

        services.AddLeaderHostedService<RegistrationProbe>(stopBudget: TimeSpan.FromSeconds(5));

        using var provider = services.BuildServiceProvider();
        var wrapper = Assert.IsType<LeaderHostedService<RegistrationProbe>>(Assert.Single(provider.GetServices<IHostedService>()));
        Assert.Equal(TimeSpan.FromSeconds(5), wrapper.StopBudget);
    }

    [Fact]
    public void A_negative_stop_budget_is_refused()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddLeaderHostedService<RegistrationProbe>(stopBudget: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void The_job_lock_check_is_a_ready_check_under_its_default_name_and_reads_the_grace_from_the_section()
    {
        var services = new ServiceCollection();

        services.AddJobLockReadiness(Config(("Health:JobLockGraceMinutes", "7")), "Health");

        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(
            provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            r => r.Name == "job-lock");
        Assert.Contains(ReadinessEndpoint.ReadyTag, registration.Tags);
        Assert.Equal(HealthRegistration.CheckTimeout, registration.Timeout);
        Assert.Equal(7, provider.GetRequiredService<IOptions<JobLockCheckOptions>>().Value.JobLockGraceMinutes);
    }

    [Fact]
    public void The_grace_is_fifteen_minutes_when_the_section_does_not_set_it()
    {
        var services = new ServiceCollection();

        services.AddJobLockReadiness(Config(), "Health");

        using var provider = services.BuildServiceProvider();
        Assert.Equal(15, provider.GetRequiredService<IOptions<JobLockCheckOptions>>().Value.JobLockGraceMinutes);
    }

    [Fact]
    public void The_readiness_check_name_can_be_given()
    {
        var services = new ServiceCollection();

        services.AddJobLockReadiness(Config(), "Health", "lock");

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations, r => r.Name == "lock");
    }

    [Fact]
    public async Task The_readiness_check_is_healthy_when_no_job_lock_is_registered()
    {
        var services = new ServiceCollection();
        services.AddJobLockReadiness(Config(), "Health");

        using var provider = services.BuildServiceProvider();
        var check = provider.GetRequiredService<JobLockHealthCheck>();

        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void A_blank_settings_section_for_the_readiness_check_is_refused()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddJobLockReadiness(Config(), " "));
    }
}
