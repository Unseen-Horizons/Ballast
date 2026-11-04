// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Ballast.Health.Tests;

/// <summary>A capped startup gate: unready before its cap, ready anyway (with one warning) after it, and met for good once met.</summary>
public class StartupGateTests
{
    private static readonly DateTime Start = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    private static (StartupGateHealthCheck Gate, ManualTime Time, CapturingLogger Logger) Make(Func<bool> met, int capSeconds)
    {
        var time = new ManualTime();
        var logger = new CapturingLogger();
        var gate = new StartupGateHealthCheck("realtime", met, TimeSpan.FromSeconds(capSeconds), Start, time, new ReadinessLog(logger, time, new HealthStart(Start)));
        return (gate, time, logger);
    }

    private static async Task<HealthStatus> StatusAsync(StartupGateHealthCheck gate) =>
        (await gate.CheckHealthAsync(new HealthCheckContext())).Status;

    [Fact]
    public async Task Unmet_before_the_cap_is_unhealthy_without_a_warning()
    {
        var (gate, time, logger) = Make(() => false, capSeconds: 60);
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(HealthStatus.Unhealthy, await StatusAsync(gate));
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Unmet_after_the_cap_is_degraded_with_one_warning()
    {
        var (gate, time, logger) = Make(() => false, capSeconds: 60);
        time.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(HealthStatus.Degraded, await StatusAsync(gate));
        Assert.Equal(HealthStatus.Degraded, await StatusAsync(gate));
        Assert.Equal(HealthStatus.Degraded, await StatusAsync(gate));
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_gate_met_before_the_cap_is_healthy()
    {
        var (gate, time, _) = Make(() => true, capSeconds: 60);
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(gate));
    }

    [Fact]
    public async Task A_gate_that_is_met_later_turns_healthy()
    {
        var met = false;
        var (gate, time, _) = Make(() => met, capSeconds: 60);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HealthStatus.Degraded, await StatusAsync(gate));

        met = true;

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(gate));
    }

    [Fact]
    public async Task Met_is_sticky_even_if_the_fact_goes_away_again()
    {
        var met = true;
        var (gate, _, _) = Make(() => met, capSeconds: 60);
        Assert.Equal(HealthStatus.Healthy, await StatusAsync(gate));

        met = false;

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(gate));
    }

    [Fact]
    public async Task A_zero_cap_is_ready_anyway_at_once()
    {
        var (gate, _, logger) = Make(() => false, capSeconds: 0);

        Assert.Equal(HealthStatus.Degraded, await StatusAsync(gate));
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }
}
