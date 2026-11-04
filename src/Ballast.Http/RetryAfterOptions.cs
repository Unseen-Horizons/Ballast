// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

namespace Ballast.Http;

/// <summary>Options for the retry added by <see cref="RetryAfterResilienceExtensions.AddRetryAfterResilience"/>.</summary>
public sealed class RetryAfterOptions
{
    /// <summary>
    /// The most tries one request gets, the first one included. Default 3, which is the first try and two retries. Must be at
    /// least 1, and 1 means no retry.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// The longest Retry-After wait honoured. A longer one is cut to this value. Default 60 seconds. Must not be below zero.
    /// </summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The waits used when a busy answer gives no Retry-After: the first for the first retry, the second for the second, and so on.
    /// A retry past the end of the list uses the last wait. Default 10 seconds, then 30 seconds. Must not be empty.
    /// </summary>
    public IReadOnlyList<TimeSpan> DefaultDelays { get; set; } = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>
    /// The statuses that say the server is busy or briefly down, and are tried again. Default 429, 502, 503 and 504. Any other status
    /// is the answer, at once.
    /// </summary>
    public IReadOnlyList<HttpStatusCode> RetryStatuses { get; set; } =
        [HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout];

    /// <summary>
    /// Whether a connection that failed (the host could not be reached) is tried again. Default true. A refusal by the safety rules
    /// is never tried again, and neither is the client's own timeout.
    /// </summary>
    public bool RetryOnConnectionFailure { get; set; } = true;
}
