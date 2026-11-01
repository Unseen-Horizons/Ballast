// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;

namespace Ballast.AspNetCore;

/// <summary>
/// A fixed-window rate limit: at most <c>limit</c> requests in each window, per partition key. The limit is read from the
/// configuration when the rate limits are added, and falls back to <see cref="DefaultPermitLimit"/> when the configuration does not
/// set it. A request over the limit is answered 429 with <c>RATE_LIMITED</c>.
/// </summary>
public sealed class FixedWindowPolicy
{
    /// <summary>
    /// Creates a policy.
    /// </summary>
    /// <param name="name">The policy's name. Required, and unique among the policies. An endpoint names it in <c>[EnableRateLimiting]</c>.</param>
    /// <param name="defaultPermitLimit">The limit when the configuration does not set one. It must be at least 1; this is checked when the rate limits are added.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or only white space.</exception>
    public FixedWindowPolicy(string name, int defaultPermitLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        Name = name;
        DefaultPermitLimit = defaultPermitLimit;
    }

    /// <summary>The policy's name.</summary>
    public string Name { get; }

    /// <summary>The limit when the configuration does not set one.</summary>
    public int DefaultPermitLimit { get; }

    /// <summary>
    /// The configuration key under <see cref="RateLimitOptions.ConfigurationSection"/>, or null for <c>{Name}PerMinute</c>. Set it when two
    /// policies share one configured limit.
    /// </summary>
    public string? ConfigurationKey { get; init; }

    /// <summary>
    /// The length of one window. It defaults to one minute and must be longer than zero; this is checked when the rate limits are added.
    /// </summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The partition key of this policy, or null to use <see cref="RateLimitOptions.PartitionKey"/>. Set it to partition this policy by
    /// its address alone, for example, when the policy must not depend on who is signed in.
    /// </summary>
    public Func<HttpContext, string>? PartitionKey { get; init; }
}
