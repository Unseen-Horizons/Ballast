// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ballast.Health.Tests;

/// <summary>The database check against a fake reachability: the grace after a loss, recovery, the cache, and the timeout.</summary>
public class DatabaseHealthCheckTests
{
    private static DatabaseHealthCheck Check(FakeReachability reachability, ManualTime time, int timeoutSeconds = 5) =>
        new(reachability, Options.Create(new HealthOptions { CheckTimeoutSeconds = timeoutSeconds }), time, NullLogger<DatabaseHealthCheck>.Instance);

    private static async Task<HealthCheckResult> RunAsync(DatabaseHealthCheck check) =>
        await check.CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task A_database_that_was_never_reachable_is_unhealthy_at_once()
    {
        var reachability = new FakeReachability { Answer = _ => Task.FromResult(false) };

        var result = await RunAsync(Check(reachability, new ManualTime()));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task After_a_loss_the_copy_is_degraded_inside_the_grace_and_unhealthy_after_it()
    {
        var time = new ManualTime();
        var reachability = new FakeReachability();
        var check = Check(reachability, time);
        Assert.Equal(HealthStatus.Healthy, (await RunAsync(check)).Status);

        time.Advance(TimeSpan.FromSeconds(16));
        reachability.Answer = _ => Task.FromResult(false);
        Assert.Equal(HealthStatus.Degraded, (await RunAsync(check)).Status);

        time.Advance(TimeSpan.FromSeconds(89));
        Assert.Equal(HealthStatus.Degraded, (await RunAsync(check)).Status);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(HealthStatus.Unhealthy, (await RunAsync(check)).Status);
    }

    [Fact]
    public async Task The_copy_is_healthy_again_once_the_database_answers()
    {
        var time = new ManualTime();
        var reachability = new FakeReachability();
        var check = Check(reachability, time);
        await RunAsync(check);

        time.Advance(TimeSpan.FromSeconds(16));
        reachability.Answer = _ => Task.FromResult(false);
        await RunAsync(check);

        time.Advance(TimeSpan.FromSeconds(16));
        reachability.Answer = _ => Task.FromResult(true);

        Assert.Equal(HealthStatus.Healthy, (await RunAsync(check)).Status);
    }

    [Fact]
    public async Task Fifty_probes_inside_the_cache_window_ask_the_database_once()
    {
        var reachability = new FakeReachability();
        var check = Check(reachability, new ManualTime());

        for (var i = 0; i < 50; i++)
        {
            await RunAsync(check);
        }

        Assert.Equal(1, reachability.Asks);
    }

    [Fact]
    public async Task A_hung_ask_is_cut_off_at_the_check_timeout()
    {
        // The ask never answers. Its wait is cancelled by the check's own timeout, which the check must treat as a failed ask.
        var reachability = new FakeReachability
        {
            Answer = async ct =>
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                return true;
            },
        };
        var check = Check(reachability, new ManualTime(), timeoutSeconds: 1);

        var result = await RunAsync(check);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, reachability.Asks);
    }

    [Fact]
    public async Task A_failing_ask_is_reported_without_its_message()
    {
        var reachability = new FakeReachability { Answer = _ => throw new InvalidOperationException("Server=db.internal;Password=hunter2") };

        var result = await RunAsync(Check(reachability, new ManualTime()));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("hunter2", result.Description ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(result.Exception);
    }
}
