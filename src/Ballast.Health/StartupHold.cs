// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Health;

/// <summary>
/// The startup hold: keeps a new copy's port closed until its readiness checks pass, for at most
/// <see cref="HealthOptions.StartupHoldMaxSeconds"/> from process start. Hosted services start in registration order, and the
/// port opens only after the last of them has returned from StartAsync, so this must be the last hosted service an app
/// registers (<see cref="HealthRegistration.AddStartupHold"/>). The platform's start check then keeps the old, warm copy serving
/// until this one is ready. The hold waits on the same checks, with the same predicate, as the readiness route, so it never waits
/// on a check that is not tagged <see cref="ReadinessEndpoint.ReadyTag"/>. It never throws: a failure is logged and the port opens.
/// </summary>
public sealed class StartupHold : IHostedService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly IOptions<HealthOptions> _options;
    private readonly HealthStart _start;
    private readonly TimeProvider _time;
    private readonly HealthCheckService _health;
    private readonly StartupWarmup _warmup;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;

    /// <summary>Creates the hold.</summary>
    /// <param name="options">The readiness settings, read for the cap. Required.</param>
    /// <param name="start">When the process started. Required.</param>
    /// <param name="time">The clock. Required.</param>
    /// <param name="health">Runs the checks. Required.</param>
    /// <param name="warmup">Started when the hold starts, so the warm-up runs while the port is closed. Required.</param>
    /// <param name="logger">Where the hold's lines go. Required.</param>
    public StartupHold(
        IOptions<HealthOptions> options,
        HealthStart start,
        TimeProvider time,
        HealthCheckService health,
        StartupWarmup warmup,
        ILogger logger)
        : this(options, start, time, health, warmup, logger, DefaultPollInterval)
    {
    }

    /// <summary>The same hold with its poll interval set. The tests use a short one.</summary>
    /// <param name="options">The readiness settings, read for the cap. Required.</param>
    /// <param name="start">When the process started. Required.</param>
    /// <param name="time">The clock. Required.</param>
    /// <param name="health">Runs the checks. Required.</param>
    /// <param name="warmup">Started when the hold starts, so the warm-up runs while the port is closed. Required.</param>
    /// <param name="logger">Where the hold's lines go. Required.</param>
    /// <param name="pollInterval">How long to wait between two rounds of checks.</param>
    public StartupHold(
        IOptions<HealthOptions> options,
        HealthStart start,
        TimeProvider time,
        HealthCheckService health,
        StartupWarmup warmup,
        ILogger logger,
        TimeSpan pollInterval)
    {
        _options = options;
        _start = start;
        _time = time;
        _health = health;
        _warmup = warmup;
        _logger = logger;
        _pollInterval = pollInterval;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var maxSeconds = _options.Value.StartupHoldMaxSeconds;
        if (maxSeconds <= 0)
        {
            return;
        }

        // Started here, not only from the started event (which fires after the port opens), or the hold would wait for a
        // warm-up that cannot start until it returns. Begin is idempotent, so the later call does nothing.
        _warmup.Begin();

        var capAt = _start.StartedUtc.AddSeconds(maxSeconds);
        try
        {
            while (true)
            {
                var report = await _health.CheckHealthAsync(IsReadinessCheck, cancellationToken);
                var utcNow = _time.GetUtcNow().UtcDateTime;

                if (report.Status != HealthStatus.Unhealthy)
                {
                    _logger.LogInformation("Startup hold: port opens after {Seconds} s, readiness {Status}", SecondsSinceStart(utcNow), report.Status);
                    return;
                }

                if (utcNow >= capAt)
                {
                    _logger.LogWarning(
                        "Startup hold: port opens at the {Cap} s cap while readiness is {Status}; the copy may be served cold",
                        maxSeconds, report.Status);
                    return;
                }

                // Never sleep past the cap.
                await Task.Delay(TimeSpan.FromTicks(Math.Min(_pollInterval.Ticks, (capAt - utcNow).Ticks)), _time, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Startup hold: the host stopped before readiness passed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup hold failed; the port opens now, with readiness not confirmed");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The same filter as the readiness route: only the checks tagged ready.</summary>
    private static bool IsReadinessCheck(HealthCheckRegistration check) => check.Tags.Contains(ReadinessEndpoint.ReadyTag);

    private int SecondsSinceStart(DateTime utcNow) => (int)(utcNow - _start.StartedUtc).TotalSeconds;
}
