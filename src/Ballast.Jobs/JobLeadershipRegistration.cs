// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Health;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Jobs;

/// <summary>
/// Wiring for the job lock. Call <see cref="AddJobLeadership"/> before the first hosted service is registered: hosted services
/// start in registration order and stop in the reverse, and the lock has to start first and stop last. The registration cannot
/// see the hosted services the app registered before it, so that order is the app's to keep (see the usage guide, "Order").
/// </summary>
public static class JobLeadershipRegistration
{
    /// <summary>The configuration key of the host's shutdown wait, in seconds: <c>Hosting:ShutdownTimeoutSeconds</c>.</summary>
    public const string ShutdownTimeoutKey = "Hosting:ShutdownTimeoutSeconds";

    /// <summary>
    /// Registers <see cref="IJobLeadership"/>. With <see cref="JobLeadershipOptions.Enabled"/> off (the default) that is
    /// <see cref="AlwaysHolderLeadership"/> and no lock is taken; with it on, it is <see cref="JobLeadership"/>, which is also a
    /// hosted service, on a connection of its own built from <paramref name="connectionString"/>. The <see cref="RequireJobHolderOptions"/>
    /// of the setup are registered either way, so that <see cref="RequireJobHolderAttribute"/> has its words.
    /// </summary>
    /// <param name="services">The app's services. Required.</param>
    /// <param name="configuration">The configuration the settings are bound from. Required.</param>
    /// <param name="resourceName">
    /// The application-lock resource both copies of the app compete for. Required, and not blank. <c>JobLeadership:ResourceName</c>
    /// overrides it when it is set.
    /// </param>
    /// <param name="connectionString">
    /// The connection string to the database that holds the lock. Required, and not blank. It is used only when the lock is on.
    /// </param>
    /// <param name="configure">Sets the setup values: the section, the application name suffix and the 409 words. Optional.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A value that must name something is blank: the resource, the connection string, the settings section, the 409 title or detail.
    /// </exception>
    public static IServiceCollection AddJobLeadership(
        this IServiceCollection services,
        IConfiguration configuration,
        string resourceName,
        string connectionString,
        Action<JobLeadershipSetup>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var setup = new JobLeadershipSetup();
        configure?.Invoke(setup);
        ArgumentException.ThrowIfNullOrWhiteSpace(setup.SettingsSection);
        ArgumentNullException.ThrowIfNull(setup.ApplicationNameSuffix);
        ArgumentException.ThrowIfNullOrWhiteSpace(setup.RequireJobHolder.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(setup.RequireJobHolder.Detail);

        var options = new JobLeadershipOptions();
        configuration.GetSection(setup.SettingsSection).Bind(options);
        var resource = string.IsNullOrWhiteSpace(options.ResourceName) ? resourceName : options.ResourceName.Trim();

        services.AddSingleton(setup.RequireJobHolder);

        if (!options.Enabled)
        {
            services.AddSingleton<IJobLeadership, AlwaysHolderLeadership>();
            return services;
        }

        var applicationNameSuffix = setup.ApplicationNameSuffix;
        services.AddSingleton(sp => new JobLeadership(
            options,
            resource,
            () => new SqlAppLockSession(connectionString, resource + applicationNameSuffix),
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp,
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetRequiredService<ILogger<JobLeadership>>()));
        services.AddSingleton<IJobLeadership>(sp => sp.GetRequiredService<JobLeadership>());
        services.AddHostedService(sp => sp.GetRequiredService<JobLeadership>());
        return services;
    }

    /// <summary>
    /// Gives the host time to finish what a stopping copy has in flight: <c>Hosting:ShutdownTimeoutSeconds</c>, or
    /// <paramref name="defaultSeconds"/> when that is not set (60 by default), instead of the host's 30. The value is never below
    /// one second. On Azure App Service the container's own grace period (<c>WEBSITES_CONTAINER_STOP_TIME_LIMIT</c>) must be set above
    /// it, so the host always finishes stopping inside it.
    /// </summary>
    /// <param name="services">The app's services. Required.</param>
    /// <param name="configuration">The configuration the wait is read from. Required.</param>
    /// <param name="defaultSeconds">The wait when the configuration does not set one. Default 60.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    public static IServiceCollection ConfigureGracefulShutdown(this IServiceCollection services, IConfiguration configuration, int defaultSeconds = 60)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var seconds = Math.Max(1, configuration.GetValue(ShutdownTimeoutKey, defaultSeconds));
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(seconds));
        return services;
    }

    /// <summary>
    /// Registers a worker that runs only while this copy holds the job lock (see <see cref="LeaderHostedService{T}"/>), in place of
    /// <c>AddHostedService&lt;T&gt;()</c>.
    /// </summary>
    /// <typeparam name="T">The worker. It must be able to start again after it stopped, which a loop in <c>ExecuteAsync</c> is.</typeparam>
    /// <param name="services">The app's services. Required.</param>
    /// <param name="factory">
    /// How to get the worker when something else already registers it as a singleton (a controller reads its counters, say).
    /// Without one the worker is registered as a singleton here.
    /// </param>
    /// <param name="once">Run the worker in at most one term, even when that term ends before it has finished.</param>
    /// <param name="stopBudget">
    /// How long the worker gets to unwind when its term ends. Default <see cref="LeaderHostedService{T}.DefaultStopBudget"/> (45 seconds).
    /// It must not be negative.
    /// </param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stopBudget"/> is negative.</exception>
    public static IServiceCollection AddLeaderHostedService<T>(
        this IServiceCollection services,
        Func<IServiceProvider, T>? factory = null,
        bool once = false,
        TimeSpan? stopBudget = null)
        where T : BackgroundService
    {
        ArgumentNullException.ThrowIfNull(services);

        var budget = stopBudget ?? LeaderHostedService<T>.DefaultStopBudget;
        if (budget < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stopBudget), budget, "A worker's stop budget cannot be negative.");
        }

        if (factory is null)
        {
            services.TryAddSingleton<T>();
            factory = sp => sp.GetRequiredService<T>();
        }

        var resolve = factory;
        services.AddHostedService(sp => new LeaderHostedService<T>(
            resolve(sp),
            sp.GetRequiredService<IJobLeadership>(),
            once,
            budget,
            sp.GetRequiredService<ILogger<LeaderHostedService<T>>>()));
        return services;
    }

    /// <summary>
    /// Adds the job-lock check to readiness, under <paramref name="checkName"/>. It carries <see cref="ReadinessEndpoint.ReadyTag"/>,
    /// so it takes part in the readiness route, the readiness log and the startup hold like every other readiness check. It never
    /// requires this copy to hold the lock (see <see cref="JobLockHealthCheck"/>). Call it once.
    /// </summary>
    /// <param name="services">The app's services. Required.</param>
    /// <param name="configuration">The configuration the grace period is bound from. Required.</param>
    /// <param name="settingsSection">
    /// The configuration section the grace period is bound from, with the key <c>JobLockGraceMinutes</c>. Required, and not blank.
    /// An app that keeps its readiness settings in <c>Health</c> passes <c>"Health"</c>.
    /// </param>
    /// <param name="checkName">The name the readiness body gives the check. Default <c>job-lock</c>.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="settingsSection"/> or <paramref name="checkName"/> is blank.</exception>
    public static IServiceCollection AddJobLockReadiness(
        this IServiceCollection services,
        IConfiguration configuration,
        string settingsSection,
        string checkName = JobLockHealthCheck.DefaultCheckName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsSection);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkName);

        services.Configure<JobLockCheckOptions>(configuration.GetSection(settingsSection));
        services.TryAddSingleton(TimeProvider.System);
        return services.AddReadinessCheck<JobLockHealthCheck>(checkName);
    }
}
