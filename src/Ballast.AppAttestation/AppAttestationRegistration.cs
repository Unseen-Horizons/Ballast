// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.RateLimiting;

using Ballast.AspNetCore;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation;

/// <summary>
/// Wires the app session into the host in one place, so the app's <c>Program.cs</c> stays a list. A test host can build the same
/// pieces without booting the database.
/// </summary>
public static class AppAttestationRegistration
{
    /// <summary>
    /// Registers the setup and the options, the validator, the token service, the nonce and ticket stores (the in-memory ones unless the
    /// app registered its own first), the verifier over the Google decoder (unless a decoder is already registered), the manager and the
    /// start-up check. Throws unless the setup is complete and <c>AppSession:Key</c> is usable, exactly like a user JWT key.
    /// </summary>
    /// <param name="services">The app's services. Required.</param>
    /// <param name="configuration">The configuration the settings are bound from. Required.</param>
    /// <param name="environment">The host environment. The Development bypass is honoured only in Development. Required.</param>
    /// <param name="configure">Sets the setup: the package, the issuer and audience, the header and cookie, the redirect prefix, the codes and the texts. Required.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The setup is incomplete (the message names the property), or the signing key is missing, a placeholder or too short.
    /// </exception>
    public static IServiceCollection AddAppAttestation(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<AppAttestationSetup> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configure);

        var setup = new AppAttestationSetup();
        configure(setup);
        setup.Validate();

        var key = StartupChecks.RequireSecret(
            configuration,
            $"{setup.SessionSection}:{nameof(AppSessionOptions.Key)}",
            AppAttestationDefaults.MinimumKeyBytes,
            AppAttestationDefaults.PlaceholderSecret);

        services.AddSingleton(setup);

        // Validated when the host starts: a lifetime outside 1 to 168 hours, or a store with no room, stops the start.
        services.AddOptions<AppSessionOptions>()
            .Bind(configuration.GetSection(setup.SessionSection))
            .Validate(o => o.LifetimeHours is >= 1 and <= AppSessionOptions.MaximumLifetimeHours,
                $"{setup.SessionSection}:{nameof(AppSessionOptions.LifetimeHours)} must be from 1 to {AppSessionOptions.MaximumLifetimeHours} hours.")
            .Validate(o => o.NonceLifetime > TimeSpan.Zero, $"{setup.SessionSection}:{nameof(AppSessionOptions.NonceLifetime)} must be longer than zero.")
            .Validate(o => o.TicketLifetime > TimeSpan.Zero, $"{setup.SessionSection}:{nameof(AppSessionOptions.TicketLifetime)} must be longer than zero.")
            .Validate(o => o.StoreCapacity >= 1, $"{setup.SessionSection}:{nameof(AppSessionOptions.StoreCapacity)} must be at least 1.")
            .ValidateOnStart();

        services.Configure<AppAttestationOptions>(configuration.GetSection(setup.AttestationSection));

        // The binder fills a string[] only from numbered keys (DeveloperCertificates:0, :1). One comma-separated setting
        // (DeveloperCertificates=a,b) is a plain value it silently ignores, which left production with "Developer certificates
        // configured: 0" (2026-10-05). The value is added here; NormalizedDeveloperDigests splits it on commas.
        services.PostConfigure<AppAttestationOptions>(options =>
        {
            var single = configuration[$"{setup.AttestationSection}:{nameof(AppAttestationOptions.DeveloperCertificates)}"];
            if (!string.IsNullOrWhiteSpace(single))
                options.DeveloperCertificates = [.. options.DeveloperCertificates, single];
        });
        services.Configure<PlayIntegrityOptions>(configuration.GetSection(setup.PlayIntegritySection));

        services.TryAddSingleton(TimeProvider.System);

        // Unattested sessions validate only in Development, the one environment that can issue them.
        services.AddSingleton(sp => new AppSessionValidator(key, setup.Issuer, setup.Audience, acceptUnattested: environment.IsDevelopment(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<AppSessionTokenService>();
        services.TryAddSingleton<IAppSessionNonceStore>(sp => new InMemoryAppSessionNonceStore(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<AppSessionOptions>>().Value.StoreCapacity));
        services.TryAddSingleton<IAppSessionTicketStore>(sp => new InMemoryAppSessionTicketStore(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<AppSessionOptions>>().Value.StoreCapacity));
        services.AddSingleton<WouldRefuseLog>();

        services.TryAddSingleton<IPlayIntegrityDecoder, GooglePlayIntegrityDecoder>();
        services.AddSingleton<PlayIntegrityVerifier>();
        services.AddScoped<AppSessionManager>();

        services.AddHostedService<AppAttestationStartupCheck>();
        return services;
    }

    /// <summary>
    /// Adds the app-session scheme beside the existing ones. The bearer scheme is untouched. Call it on the authentication builder.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static AuthenticationBuilder AddAppAttestationScheme(this AuthenticationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddScheme<AuthenticationSchemeOptions, AppSessionAuthenticationHandler>(AppAttestationDefaults.SchemeName, _ => { });
    }

    /// <summary>
    /// Adds the gate. Put it after <c>UseAuthentication</c> (it reads the user to let a signed-in admin through) and before
    /// <c>UseRateLimiter</c> (which partitions on the session the gate parks on the request).
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IApplicationBuilder UseAppAttestationGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<AppSessionGateMiddleware>();
    }

    /// <summary>
    /// Adds a fixed-window limit to the rate limits, partitioned by the caller's session, then their signed-in user, then their address
    /// (<see cref="AppSessionContext.RateLimitKey"/>). The bootstrap routes are the usual users of it: they are cheap for the caller and
    /// costly for Google.
    /// </summary>
    /// <param name="options">The rate-limit options of <c>AddRateLimits</c>.</param>
    /// <param name="policyName">The name an endpoint gives in <c>[EnableRateLimiting]</c>. Required.</param>
    /// <param name="defaultPermitPerMinute">The limit when the configuration does not set one. At least 1.</param>
    /// <param name="configurationKey">The key under the rate-limit section, or null for <c>{policyName}PerMinute</c>.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is blank.</exception>
    public static RateLimitOptions AddAppAttestationPolicy(this RateLimitOptions options, string policyName, int defaultPermitPerMinute, string? configurationKey = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        options.Policies.Add(new FixedWindowPolicy(policyName, defaultPermitPerMinute)
        {
            ConfigurationKey = configurationKey,
            PartitionKey = AppSessionContext.RateLimitKey,
        });
        return options;
    }

    /// <summary>
    /// The same limit on ASP.NET's own rate limiter, for a host that does not use <c>AddRateLimits</c>. A fixed window of one minute,
    /// partitioned as <see cref="AddAppAttestationPolicy"/> partitions.
    /// </summary>
    /// <param name="limiter">The rate limiter's options.</param>
    /// <param name="policyName">The name an endpoint gives in <c>[EnableRateLimiting]</c>. Required.</param>
    /// <param name="permitPerMinute">The limit. At least 1.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="permitPerMinute"/> is below 1.</exception>
    public static RateLimiterOptions AddAppAttestationLimit(this RateLimiterOptions limiter, string policyName, int permitPerMinute)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentOutOfRangeException.ThrowIfLessThan(permitPerMinute, 1);

        limiter.AddPolicy(policyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(AppSessionContext.RateLimitKey(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
        return limiter;
    }
}
