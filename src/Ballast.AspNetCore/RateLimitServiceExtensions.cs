// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Threading.RateLimiting;

using Ballast.Errors;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Ballast.AspNetCore;

/// <summary>Registers the fixed-window rate limits and the answer to a rejected request.</summary>
public static class RateLimitServiceExtensions
{
    // The answer for a rejection whose lease gives no retry time. A fixed window of one minute gives this value.
    private const int DefaultRetryAfterSeconds = 60;

    /// <summary>
    /// Registers the rate limits and the rejection. Each limit is read from the configuration now, so a limit of 0 or less, a window
    /// of zero or less, or a value that is not a whole number stops the start, with a message that names the policy. The application
    /// still calls <c>UseRateLimiter</c> in its pipeline, after authentication.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configuration">The configuration that holds the limits.</param>
    /// <param name="configure">Sets the options, or null for none.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A policy has a limit of 0 or less, a window of zero or less, a limit that is not a whole number, or a name that is already used.
    /// The message names the policy and the configuration key.
    /// </exception>
    public static IServiceCollection AddRateLimits(this IServiceCollection services, IConfiguration configuration, Action<RateLimitOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RateLimitOptions();
        configure?.Invoke(options);

        var claimTypes = options.UserIdClaimTypes.ToArray();
        Func<HttpContext, string> defaultKey = options.PartitionKey ?? (context => RateLimitKeys.ForUserOrAddress(context, claimTypes));
        var rejectionDetail = options.RejectionDetail;

        var globalPolicy = options.GlobalPolicy is null
            ? null
            : Resolve(options.GlobalPolicy, options.ConfigurationSection, configuration, defaultKey);

        var names = new HashSet<string>(StringComparer.Ordinal);
        List<ResolvedPolicy> named = [];
        foreach (var policy in options.Policies)
        {
            if (!names.Add(policy.Name))
            {
                throw new InvalidOperationException($"The rate limit policy '{policy.Name}' is set more than once. Each policy name must be unique.");
            }

            named.Add(Resolve(policy, options.ConfigurationSection, configuration, defaultKey));
        }

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, token) => new ValueTask(WriteRejectionAsync(context, rejectionDetail, token));

            if (globalPolicy is not null)
            {
                limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(globalPolicy.Key(context), _ => WindowOptions(globalPolicy)));
            }

            foreach (var policy in named)
            {
                limiter.AddPolicy<string>(policy.Name, context =>
                    RateLimitPartition.GetFixedWindowLimiter(policy.Key(context), _ => WindowOptions(policy)));
            }
        });

        return services;
    }

    private static ResolvedPolicy Resolve(FixedWindowPolicy policy, string section, IConfiguration configuration, Func<HttpContext, string> defaultKey)
    {
        var path = ConfigurationPath.Combine(section, policy.ConfigurationKey ?? policy.Name + "PerMinute");
        var permitLimit = policy.DefaultPermitLimit;

        var configured = configuration[path];
        if (configured is not null && !int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out permitLimit))
        {
            throw new InvalidOperationException($"The rate limit policy '{policy.Name}' reads {path}, which must be a whole number of requests. It is '{configured}'.");
        }

        if (permitLimit < 1)
        {
            throw new InvalidOperationException($"The rate limit policy '{policy.Name}' has a limit of {permitLimit}, and a limit must be at least 1. Set {path} to a number from 1 up, or give the policy a default above 0.");
        }

        if (policy.Window <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"The rate limit policy '{policy.Name}' has a window of {policy.Window}, and a window must be longer than zero.");
        }

        return new ResolvedPolicy(policy.Name, policy.PartitionKey ?? defaultKey, permitLimit, policy.Window);
    }

    private static FixedWindowRateLimiterOptions WindowOptions(ResolvedPolicy policy) => new()
    {
        PermitLimit = policy.PermitLimit,
        Window = policy.Window,
        QueueLimit = 0,
    };

    private static async Task WriteRejectionAsync(OnRejectedContext context, Func<HttpContext, string> detail, CancellationToken token)
    {
        var http = context.HttpContext;
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : DefaultRetryAfterSeconds;
        http.Response.Headers.RetryAfter = new StringValues(seconds.ToString(CultureInfo.InvariantCulture));

        var problem = new Problem
        {
            Type = ProblemTypes.For(429),
            Title = ProblemResponses.TitlesOf(http).Title(429, ErrorCodes.RateLimited),
            Status = 429,
            Detail = detail(http),
            Instance = http.Request.Path.Value,
            Code = ErrorCodes.RateLimited,
            TraceId = ProblemResponses.TraceIdOf(http),
        };
        await ProblemResponses.WriteAsync(http, problem, token);
    }

    private sealed record ResolvedPolicy(string Name, Func<HttpContext, string> Key, int PermitLimit, TimeSpan Window);
}
