// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Health;

/// <summary>
/// The wiring for readiness. Call <see cref="AddBallastHealth"/> first, then add the checks the app needs, and the startup
/// hold last. Every check added here carries <see cref="ReadinessEndpoint.ReadyTag"/> and is a singleton, because a stateful
/// check must keep its state between probes. A check from outside this library (another library's, or one of the app's own)
/// joins readiness the same way, through <see cref="AddReadinessCheck{THealthCheck}"/>.
/// </summary>
public static class HealthRegistration
{
    /// <summary>
    /// The limit the health service puts on one readiness check. It is above the probes' own five-second limit, so a slow
    /// database probe is answered by its own timeout and grace rather than reported as a timed-out check.
    /// </summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private const string WarmupGateDefaultName = "warmup";

    /// <summary>
    /// Registers what readiness needs: the <see cref="HealthOptions"/> settings, the process start, the build identity, the
    /// readiness log and the warm-up runner. Call it once, before any other method of this class.
    /// </summary>
    /// <param name="services">The application's services. Required.</param>
    /// <param name="configuration">The configuration the settings are bound from. Required.</param>
    /// <param name="configure">Sets the code-time values (section, metadata key, log category, build assembly). Optional.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException">A setup value that must name something is blank.</exception>
    /// <exception cref="InvalidOperationException">This method was already called on these services.</exception>
    public static IServiceCollection AddBallastHealth(this IServiceCollection services, IConfiguration configuration, Action<HealthSetupOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(d => d.ServiceType == typeof(HealthStart)))
        {
            throw new InvalidOperationException("AddBallastHealth is already registered on these services. Call it once.");
        }

        var setup = new HealthSetupOptions();
        configure?.Invoke(setup);
        ArgumentException.ThrowIfNullOrWhiteSpace(setup.SettingsSection, nameof(setup.SettingsSection));
        ArgumentException.ThrowIfNullOrWhiteSpace(setup.BuiltAtMetadataKey, nameof(setup.BuiltAtMetadataKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(setup.LogCategory, nameof(setup.LogCategory));

        services.AddHealthChecks();
        services.Configure<HealthOptions>(configuration.GetSection(setup.SettingsSection));
        services.TryAddSingleton(TimeProvider.System);

        var start = new HealthStart(TimeProvider.System.GetUtcNow().UtcDateTime);
        var assembly = setup.BuildAssembly ?? Assembly.GetEntryAssembly() ?? typeof(BuildInfoReader).Assembly;
        var build = BuildInfoReader.Read(assembly, setup.BuiltAtMetadataKey);
        var category = setup.LogCategory;

        services.AddSingleton(start);
        services.AddSingleton(build);
        services.AddSingleton(new HealthLogCategory(category));
        services.AddSingleton(sp => new ReadinessLog(sp.GetRequiredService<ILoggerFactory>().CreateLogger(category), sp.GetRequiredService<TimeProvider>(), start));
        services.AddSingleton(sp => new StartupWarmup(
            sp.GetServices<IWarmupStep>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(category),
            build,
            start));
        return services;
    }

    /// <summary>
    /// The database check: whether the context's database answers, asked at most once per cache window. It is registered once
    /// per application, for one context. A second call is refused, so that two contexts never share one check.
    /// </summary>
    /// <typeparam name="TContext">The application's DbContext.</typeparam>
    /// <param name="services">The services <see cref="AddBallastHealth"/> was called on. Required.</param>
    /// <param name="name">The name the readiness body gives the check. Required, and not blank.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A database check is already registered.</exception>
    public static IServiceCollection AddDatabaseReadiness<TContext>(this IServiceCollection services, string name = "database") where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RefuseSecond<DatabaseHealthCheck>(services, "A database readiness check");

        services.AddSingleton<IDatabaseReachability, EfDatabaseReachability<TContext>>();
        services.AddSingleton<DatabaseHealthCheck>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(name, tags: [ReadinessEndpoint.ReadyTag], timeout: CheckTimeout);
        return services;
    }

    /// <summary>
    /// The schema check: the code's migrations against the database's history. It is registered once per application, for one
    /// context. A second call is refused.
    /// </summary>
    /// <typeparam name="TContext">The application's DbContext.</typeparam>
    /// <param name="services">The services <see cref="AddBallastHealth"/> was called on. Required.</param>
    /// <param name="name">The name the readiness body gives the check. Required, and not blank.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A schema check is already registered.</exception>
    public static IServiceCollection AddSchemaReadiness<TContext>(this IServiceCollection services, string name = "schema") where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RefuseSecond<SchemaHealthCheck>(services, "A schema readiness check");

        services.AddSingleton<IMigrationSource, EfMigrationSource<TContext>>();
        services.AddSingleton<SchemaHealthCheck>();
        services.AddHealthChecks().AddCheck<SchemaHealthCheck>(name, tags: [ReadinessEndpoint.ReadyTag], timeout: CheckTimeout);
        return services;
    }

    /// <summary>
    /// A capped startup gate under <paramref name="name"/>: Unhealthy until <paramref name="met"/> holds, for at most
    /// <paramref name="maxWait"/> from process start, then Degraded (ready anyway, with one warning in the log). Once met it stays
    /// met. Each gate has its own cap, so a slow fact delays a deploy by its own cap and no longer.
    /// </summary>
    /// <param name="services">The services <see cref="AddBallastHealth"/> was called on. Required.</param>
    /// <param name="name">The gate's name in the readiness body and the log. Required, and not blank, and unique.</param>
    /// <param name="met">Says whether the fact holds. It is asked with the root services, so it must read singletons only. Required.</param>
    /// <param name="maxWait">The cap, measured from process start. Zero or more; zero means the gate is ready anyway at once.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWait"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">A gate with the same name is already registered.</exception>
    public static IServiceCollection AddStartupGate(this IServiceCollection services, string name, Func<IServiceProvider, bool> met, TimeSpan maxWait)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(met);
        if (maxWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWait), maxWait, "A startup gate's maximum wait cannot be negative.");
        }

        return AddGate(services, name, met, maxWait);
    }

    /// <summary>
    /// The warm-up gate, named <c>warmup</c> by default: met when the warm-up steps have finished, and capped at
    /// <paramref name="maxWait"/> from process start, like every capped gate.
    /// </summary>
    /// <param name="services">The services <see cref="AddBallastHealth"/> was called on. Required.</param>
    /// <param name="maxWait">The cap, measured from process start. Zero or more.</param>
    /// <param name="name">The gate's name. Not blank. Default <c>warmup</c>.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWait"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">A gate with the same name is already registered.</exception>
    public static IServiceCollection AddWarmupGate(this IServiceCollection services, TimeSpan maxWait, string name = WarmupGateDefaultName) =>
        AddStartupGate(services, name, sp => sp.GetRequiredService<StartupWarmup>().IsComplete, maxWait);

    /// <summary>
    /// A startup gate that never stops waiting. It is Unhealthy until <paramref name="met"/> holds, however long that takes, and it
    /// is never Degraded. It is a separate method on purpose: a capped gate is the default, and an uncapped one must be chosen.
    /// </summary>
    /// <remarks>
    /// Dangerous: if the fact never holds, this copy never becomes ready. The platform keeps the old copy serving, and the startup
    /// hold gives up only at <see cref="HealthOptions.StartupHoldMaxSeconds"/>, after which the port opens while the check is still
    /// Unhealthy. Use it only for a fact that a deploy cannot do without (for example, a startup task that every request depends
    /// on), and only when that fact is guaranteed to hold within the time the deploy allows.
    /// </remarks>
    /// <param name="services">The services <see cref="AddBallastHealth"/> was called on. Required.</param>
    /// <param name="name">The gate's name. Required, and not blank, and unique.</param>
    /// <param name="met">Says whether the fact holds. It is asked with the root services, so it must read singletons only. Required.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A gate with the same name is already registered.</exception>
    public static IServiceCollection AddUncappedStartupGate(this IServiceCollection services, string name, Func<IServiceProvider, bool> met)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(met);

        // A cap no wait can reach: the gate is never Degraded, and its one warning never comes.
        return AddGate(services, name, met, TimeSpan.MaxValue);
    }

    /// <summary>
    /// Adds a check from outside this library to readiness. It is a singleton, and it carries <see cref="ReadinessEndpoint.ReadyTag"/>,
    /// so it takes part in the readiness route, the readiness log and the startup hold, exactly as the checks above do.
    /// </summary>
    /// <typeparam name="THealthCheck">The check. A singleton, resolved from the services.</typeparam>
    /// <param name="services">The services <see cref="AddBallastHealth"/> was called on. Required.</param>
    /// <param name="name">The name the readiness body gives the check. Required, and not blank, and unique.</param>
    /// <returns>The same services, for chaining.</returns>
    public static IServiceCollection AddReadinessCheck<THealthCheck>(this IServiceCollection services, string name) where THealthCheck : class, IHealthCheck
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.AddSingleton<THealthCheck>();
        services.AddHealthChecks().AddCheck<THealthCheck>(name, tags: [ReadinessEndpoint.ReadyTag], timeout: CheckTimeout);
        return services;
    }

    /// <summary>
    /// The startup hold (<see cref="StartupHold"/>): the port stays closed until readiness passes, for at most
    /// <see cref="HealthOptions.StartupHoldMaxSeconds"/>. It must be registered last, after every hosted service and the app's own
    /// services, because the port opens only after the last hosted service has started. With the hold at zero (the default) it
    /// does nothing.
    /// </summary>
    /// <param name="services">The services <see cref="AddBallastHealth"/> was called on. Required.</param>
    /// <returns>The same services, for chaining.</returns>
    public static IServiceCollection AddStartupHold(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService(sp => new StartupHold(
            sp.GetRequiredService<IOptions<HealthOptions>>(),
            sp.GetRequiredService<HealthStart>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<HealthCheckService>(),
            sp.GetRequiredService<StartupWarmup>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(sp.GetRequiredService<HealthLogCategory>().Name)));
        return services;
    }

    private static IServiceCollection AddGate(IServiceCollection services, string name, Func<IServiceProvider, bool> met, TimeSpan maxWait)
    {
        // Each gate is a keyed singleton, so that a stateful gate keeps its state between probes. The readiness check registered
        // below resolves that singleton from each probe's scope.
        if (services.Any(d => d.ServiceType == typeof(IHealthCheck) && Equals(d.ServiceKey, name)))
        {
            throw new InvalidOperationException($"A readiness check named '{name}' is already registered. Give each check its own name.");
        }

        services.AddKeyedSingleton<IHealthCheck>(name, (sp, _) => new StartupGateHealthCheck(
            name,
            () => met(sp),
            maxWait,
            sp.GetRequiredService<HealthStart>().StartedUtc,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ReadinessLog>()));

        services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Add(new HealthCheckRegistration(
            name,
            sp => sp.GetRequiredKeyedService<IHealthCheck>(name),
            failureStatus: null,
            tags: [ReadinessEndpoint.ReadyTag],
            timeout: CheckTimeout)));
        return services;
    }

    private static void RefuseSecond<T>(IServiceCollection services, string what)
    {
        if (services.Any(d => d.ServiceType == typeof(T) && d.ServiceKey is null))
        {
            throw new InvalidOperationException($"{what} is already registered. Register it once per application.");
        }
    }
}

/// <summary>The log category the readiness lines are written under, kept so that the startup hold can write under the same one.</summary>
/// <param name="Name">The category name.</param>
internal sealed record HealthLogCategory(string Name);
