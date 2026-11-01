// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ballast.Jobs;

/// <summary>
/// The job-lock readiness check. It asks whether this copy is a sensible part of the job lock. It never requires this copy to hold
/// the lock: the old copy only stops after the new one is ready, so a rule that needed the lock would deadlock every deploy.
/// <para>
/// Healthy when the lock is off, when this copy holds it, when another copy holds it (<c>HeldByOther</c>, the normal state of a
/// deploy's new copy), while it is taking it (<c>Acquired</c>: the term-start hooks can take minutes), and while it is stopping
/// (<c>Released</c>). Otherwise it counts a streak: Healthy under 5 minutes, Degraded from 5, Unhealthy at
/// <see cref="JobLockCheckOptions.JobLockGraceMinutes"/>. The streak starts at process start when this copy has never been good,
/// and at the first bad answer otherwise. Any good answer ends it.
/// </para>
/// </summary>
public sealed class JobLockHealthCheck : IHealthCheck
{
    /// <summary>The name the check is registered under by default: <c>job-lock</c>.</summary>
    public const string DefaultCheckName = "job-lock";

    private static readonly TimeSpan DegradedAfter = TimeSpan.FromMinutes(5);

    private readonly IOptions<JobLockCheckOptions> _options;
    private readonly TimeProvider _time;
    private readonly IJobLeadership? _leadership;
    private readonly object _gate = new();
    private bool _wasGood;
    private DateTime? _badSinceUtc;

    /// <summary>Creates the check. The registration makes it a singleton, because the streak is kept between probes.</summary>
    /// <param name="options">The grace period.</param>
    /// <param name="time">The clock the streak is measured on.</param>
    /// <param name="leadership">The job lock, or null when none is registered: then the check is Healthy.</param>
    public JobLockHealthCheck(IOptions<JobLockCheckOptions> options, TimeProvider time, IJobLeadership? leadership = null)
    {
        _options = options;
        _time = time;
        _leadership = leadership;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(Evaluate());

    private HealthCheckResult Evaluate()
    {
        lock (_gate)
        {
            // No job lock registered means nothing to wait for.
            if (_leadership is null || IsGood(_leadership))
            {
                _wasGood = true;
                _badSinceUtc = null;
                return HealthCheckResult.Healthy("This copy is in order with the job lock.");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            _badSinceUtc ??= _wasGood ? now : _leadership.StartedUtc;
            var badFor = now - _badSinceUtc.Value;
            var minutes = (int)badFor.TotalMinutes;
            var last = _leadership.LastOutcome;

            if (badFor >= TimeSpan.FromMinutes(_options.Value.JobLockGraceMinutes))
            {
                return HealthCheckResult.Unhealthy($"This copy has neither held nor waited for the job lock for {minutes} min (last outcome: {last}).");
            }

            if (badFor >= DegradedAfter)
            {
                return HealthCheckResult.Degraded($"This copy has neither held nor waited for the job lock for {minutes} min (last outcome: {last}).");
            }

            return HealthCheckResult.Healthy($"The job lock was last {last}; not yet a concern.");
        }
    }

    private static bool IsGood(IJobLeadership leadership) =>
        !leadership.Enabled
        || leadership.IsHolder
        || leadership.LastOutcome is JobLeadershipOutcome.HeldByOther or JobLeadershipOutcome.Acquired or JobLeadershipOutcome.Released;
}
