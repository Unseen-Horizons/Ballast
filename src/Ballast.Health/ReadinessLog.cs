// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Ballast.Health;

/// <summary>
/// What readiness did, in the logs. It says what a copy waited on (at most every 15 s), when it first became ready, when it
/// went unready after that, and the first few distinct probes that asked (their user agent, scheme and answer), so the log
/// shows what the platform's warm-up probe really sends. The lines are written under the log category the setup names.
/// </summary>
public sealed class ReadinessLog
{
    private const int MaxProbeOrigins = 5;
    private const int MaxUserAgentLength = 200;
    private static readonly TimeSpan WaitingLogInterval = TimeSpan.FromSeconds(15);

    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly DateTime _startedUtc;
    private readonly HashSet<string> _origins = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private DateTime? _lastWaitingUtc;
    private bool _everReady;
    private bool _unreadyNow;

    /// <summary>Creates the log.</summary>
    /// <param name="logger">Where the lines go. Required.</param>
    /// <param name="time">The clock for the waiting interval and the ready time. Required.</param>
    /// <param name="start">When the process started. Required.</param>
    public ReadinessLog(ILogger logger, TimeProvider time, HealthStart start)
    {
        _logger = logger;
        _time = time;
        _startedUtc = start.StartedUtc;
    }

    /// <summary>Records one probe's answer, and any change in readiness that it shows.</summary>
    /// <param name="context">The probe's request and response.</param>
    /// <param name="report">The readiness report the probe was answered with.</param>
    public void Observe(HttpContext context, HealthReport report)
    {
        var utcNow = _time.GetUtcNow().UtcDateTime;
        lock (_gate)
        {
            RecordOrigin(context);

            if (report.Status == HealthStatus.Unhealthy)
            {
                var waiting = Names(report, HealthStatus.Unhealthy);
                if (!_everReady)
                {
                    if (_lastWaitingUtc is null || utcNow - _lastWaitingUtc.Value >= WaitingLogInterval)
                    {
                        _logger.LogInformation("Not ready, waiting on: {Checks}", waiting);
                        _lastWaitingUtc = utcNow;
                    }
                }
                else if (!_unreadyNow)
                {
                    _logger.LogInformation("Not ready any more, after being ready: {Checks}", waiting);
                }

                _unreadyNow = true;
                return;
            }

            var degraded = Names(report, HealthStatus.Degraded);
            if (!_everReady)
            {
                _everReady = true;
                _logger.LogInformation("Ready after {Seconds} s (degraded: {Degraded})", (int)(utcNow - _startedUtc).TotalSeconds, degraded);
            }
            else if (_unreadyNow)
            {
                _logger.LogInformation("Ready again (degraded: {Degraded})", degraded);
            }

            _unreadyNow = false;
        }
    }

    /// <summary>A capped startup gate passed its cap without being met. The copy is ready anyway, and the log says so once.</summary>
    /// <param name="gate">The gate's name.</param>
    /// <param name="seconds">How long the gate waited, in whole seconds.</param>
    public void GateNotMet(string gate, int seconds) =>
        _logger.LogWarning("Startup gate {Gate} was not met after {Seconds} s; reporting ready anyway", gate, seconds);

    private void RecordOrigin(HttpContext context)
    {
        if (_origins.Count >= MaxProbeOrigins)
        {
            return;
        }

        var request = context.Request;
        var userAgent = request.Headers["User-Agent"].ToString();
        if (userAgent.Length > MaxUserAgentLength)
        {
            userAgent = userAgent[..MaxUserAgentLength];
        }
        var forwarded = request.Headers.ContainsKey("X-Forwarded-Proto");
        var status = context.Response.StatusCode;

        if (!_origins.Add($"{userAgent}|{request.Scheme}|{forwarded}|{status}"))
        {
            return;
        }

        _logger.LogInformation(
            "Readiness probe: user agent {UserAgent}, scheme {Scheme}, X-Forwarded-Proto {Forwarded}, answered {Status}",
            userAgent, request.Scheme, forwarded ? "present" : "absent", status);
    }

    private static string Names(HealthReport report, HealthStatus status)
    {
        var names = report.Entries.Where(e => e.Value.Status == status).Select(e => e.Key).Order(StringComparer.Ordinal).ToArray();
        return names.Length == 0 ? "none" : string.Join(", ", names);
    }
}
