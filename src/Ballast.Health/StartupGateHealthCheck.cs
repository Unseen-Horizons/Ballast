// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ballast.Health;

/// <summary>
/// A startup gate: a fact that becomes true once during startup (the border index is built, the first realtime round is in,
/// the warm-up is done). Until it is met the check is Unhealthy, but only for its maximum wait from process start. After that
/// it is Degraded, which still answers 200, and the log gets one warning. So a gate that never opens delays a deploy by its
/// cap and no longer. Once met it stays Healthy, whatever the fact does later.
/// </summary>
/// <remarks>
/// An app registers a gate with <see cref="HealthRegistration.AddStartupGate"/>, which builds this class, or with
/// <see cref="HealthRegistration.AddUncappedStartupGate"/>, which never ends the wait. The class is public so that an app can
/// also construct one, or subclass it.
/// </remarks>
public class StartupGateHealthCheck : IHealthCheck
{
    private readonly string _name;
    private readonly Func<bool> _met;
    private readonly TimeSpan _maxWait;
    private readonly DateTime _startedUtc;
    private readonly TimeProvider _time;
    private readonly ReadinessLog _log;
    private readonly object _gate = new();
    private bool _metOnce;
    private bool _warned;

    /// <summary>Creates the gate.</summary>
    /// <param name="name">The gate's name, as the readiness body and the log name it. Required.</param>
    /// <param name="met">Says whether the fact holds now. It is asked until it first holds, and never after. Required.</param>
    /// <param name="maxWait">How long from <paramref name="startedUtc"/> the gate may stay Unhealthy. Zero or more.</param>
    /// <param name="startedUtc">When the process started, in UTC. The cap is measured from it.</param>
    /// <param name="time">The clock. Required.</param>
    /// <param name="log">Where the one warning goes when the cap passes. Required.</param>
    public StartupGateHealthCheck(string name, Func<bool> met, TimeSpan maxWait, DateTime startedUtc, TimeProvider time, ReadinessLog log)
    {
        _name = name;
        _met = met;
        _maxWait = maxWait;
        _startedUtc = startedUtc;
        _time = time;
        _log = log;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(Check());

    private HealthCheckResult Check()
    {
        lock (_gate)
        {
            if (_metOnce || _met())
            {
                _metOnce = true;
                return HealthCheckResult.Healthy($"{_name} is met.");
            }

            var waited = _time.GetUtcNow().UtcDateTime - _startedUtc;
            if (waited < _maxWait)
            {
                return HealthCheckResult.Unhealthy($"Waiting for {_name}.");
            }

            var seconds = (int)waited.TotalSeconds;
            if (!_warned)
            {
                _warned = true;
                _log.GateNotMet(_name, seconds);
            }

            return HealthCheckResult.Degraded($"{_name} was not met after {seconds} s; reporting ready anyway.");
        }
    }
}
