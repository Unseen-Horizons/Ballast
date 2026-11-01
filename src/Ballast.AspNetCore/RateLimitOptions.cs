// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

using Microsoft.AspNetCore.Http;

namespace Ballast.AspNetCore;

/// <summary>
/// The options of the rate limits. They are set once, in <see cref="RateLimitServiceExtensions.AddRateLimits"/>.
/// </summary>
public sealed class RateLimitOptions
{
    private const string DefaultRejectionDetail = "Rate limit exceeded. Retry after the window resets.";

    /// <summary>
    /// The configuration section that holds the permit limits. The limit of a policy is read from
    /// <c>{section}:{ConfigurationKey}</c>. The default is <c>RateLimits</c>.
    /// </summary>
    public string ConfigurationSection { get; set; } = "RateLimits";

    /// <summary>
    /// The limit that applies to every request, whatever its endpoint, or null for none. Its configuration key defaults to
    /// <c>{Name}PerMinute</c>, so a global policy named <c>Global</c> reads <c>GlobalPerMinute</c>.
    /// </summary>
    public FixedWindowPolicy? GlobalPolicy { get; set; }

    /// <summary>
    /// The named policies. An endpoint names one with <c>[EnableRateLimiting("name")]</c>. Each name is unique. Empty by default.
    /// </summary>
    public IList<FixedWindowPolicy> Policies { get; } = [];

    /// <summary>
    /// The claim types that hold a signed-in user's id, tried in order, for the default partition key. The default is <c>sub</c>,
    /// then the name identifier claim. A caller with none of them is partitioned by its address.
    /// </summary>
    public IList<string> UserIdClaimTypes { get; } = ["sub", ClaimTypes.NameIdentifier];

    /// <summary>
    /// The partition key of every policy that has no partition key of its own, or null for the default: <c>user:{id}</c> for a signed-in
    /// caller, else <c>ip:{address}</c> (see <see cref="RateLimitKeys.ForUserOrAddress"/>). An application that partitions by something
    /// else, such as a session it has checked, sets it here.
    /// </summary>
    public Func<HttpContext, string>? PartitionKey { get; set; }

    /// <summary>
    /// The detail of the 429 answer, as a function of the request so the text can be localised. The default is
    /// "Rate limit exceeded. Retry after the window resets.".
    /// </summary>
    public Func<HttpContext, string> RejectionDetail { get; set; } = static _ => DefaultRejectionDetail;
}
