// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Health.Tests;

/// <summary>
/// The startup hold, through a real <see cref="HealthCheckService"/> with scripted checks and the real warm-up. It covers when
/// the port is let open (Healthy, Degraded, the cap, cancellation), what the log says, that a throwing check never makes the
/// start throw, and that the warm-up begins before the first check. The clock is <see cref="ManualTime"/>, and the checks
/// move it, so the cap is exact; the poll interval is a few milliseconds.
/// </summary>
public class StartupHoldTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(5);

    // A bound on every await, so a hold that never returns fails the test instead of hanging the run.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private static StartupHold Make(
        ManualTime time,
        int capSeconds,
        Func<CancellationToken, Task<HealthCheckResult>> check,
        CapturingLogger logger,
        RecordingWarmupStep step)
    {
        var start = new HealthStart(time.GetUtcNow().UtcDateTime);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddCheck("gate", new ScriptedCheck(check), tags: [ReadinessEndpoint.ReadyTag])

            // Not tagged ready and always Unhealthy: the hold must not wait on it, any more than the readiness route does.
            .AddCheck("untagged", new ScriptedCheck(_ => Task.FromResult(HealthCheckResult.Unhealthy())));
        var health = services.BuildServiceProvider().GetRequiredService<HealthCheckService>();

        var warmup = new StartupWarmup(
            [step],
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new TestLifetime(),
            new CapturingLogger(),
            new BuildInfo(null, null),
            start);

        return new StartupHold(
            Options.Create(new HealthOptions { StartupHoldMaxSeconds = capSeconds }),
            start,
            time,
            health,
            warmup,
            logger,
            Poll);
    }

    [Fact]
    public async Task Off_at_zero_returns_at_once_and_does_not_start_the_warm_up()
    {
        var checks = 0;
        var logger = new CapturingLogger();
        var step = new RecordingWarmupStep();
        var hold = Make(new ManualTime(), 0, _ =>
        {
            Interlocked.Increment(ref checks);
            return Task.FromResult(HealthCheckResult.Healthy());
        }, logger, step);

        await hold.StartAsync(CancellationToken.None).WaitAsync(Bound);
        await Task.Delay(100);

        Assert.Equal(0, checks);
        Assert.Equal(0, step.Runs);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Returns_on_the_first_healthy_check_and_logs_when_the_port_opens()
    {
        var checks = 0;
        var logger = new CapturingLogger();
        var hold = Make(new ManualTime(), 90, _ =>
        {
            Interlocked.Increment(ref checks);
            return Task.FromResult(HealthCheckResult.Healthy());
        }, logger, new RecordingWarmupStep());

        await hold.StartAsync(CancellationToken.None).WaitAsync(Bound);

        Assert.Equal(1, checks);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Information && e.Message == "Startup hold: port opens after 0 s, readiness Healthy");
    }

    [Fact]
    public async Task Returns_on_the_first_degraded_check()
    {
        var checks = 0;
        var logger = new CapturingLogger();
        var hold = Make(new ManualTime(), 90, _ =>
        {
            Interlocked.Increment(ref checks);
            return Task.FromResult(HealthCheckResult.Degraded());
        }, logger, new RecordingWarmupStep());

        await hold.StartAsync(CancellationToken.None).WaitAsync(Bound);

        Assert.Equal(1, checks);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Information && e.Message == "Startup hold: port opens after 0 s, readiness Degraded");
    }

    [Fact]
    public async Task Stays_while_unhealthy_and_returns_at_the_cap_with_one_warning()
    {
        var time = new ManualTime();
        var checks = 0;
        var logger = new CapturingLogger();
        var hold = Make(time, 3, _ =>
        {
            Interlocked.Increment(ref checks);
            time.Advance(TimeSpan.FromSeconds(1));
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }, logger, new RecordingWarmupStep());

        await hold.StartAsync(CancellationToken.None).WaitAsync(Bound);

        // The checks run at 0, 1 and 2 s; the third leaves the clock at the 3 s cap.
        Assert.Equal(3, checks);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("Startup hold: port opens at the 3 s cap while readiness is Unhealthy; the copy may be served cold", warning.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Returns_once_readiness_turns_healthy_and_never_warns()
    {
        var checks = 0;
        var logger = new CapturingLogger();
        var hold = Make(new ManualTime(), 60, _ =>
            Task.FromResult(Interlocked.Increment(ref checks) < 3 ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy()),
            logger, new RecordingWarmupStep());

        await hold.StartAsync(CancellationToken.None).WaitAsync(Bound);

        Assert.Equal(3, checks);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Information && e.Message == "Startup hold: port opens after 0 s, readiness Healthy");
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Returns_at_once_when_the_host_stops_during_the_hold()
    {
        using var cts = new CancellationTokenSource();
        var checks = 0;
        var logger = new CapturingLogger();
        var hold = Make(new ManualTime(), 60, _ =>
        {
            if (Interlocked.Increment(ref checks) == 1)
            {
                cts.Cancel();
            }
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }, logger, new RecordingWarmupStep());

        // The cap is 60 s and the clock never moves, so only the cancellation can end the hold.
        await hold.StartAsync(cts.Token).WaitAsync(Bound);

        Assert.Equal(1, checks);
        Assert.Single(logger.Entries, e => e.Message == "Startup hold: the host stopped before readiness passed");
    }

    [Fact]
    public async Task A_check_that_throws_does_not_make_StartAsync_throw()
    {
        var time = new ManualTime();
        var checks = 0;
        var logger = new CapturingLogger();
        var hold = Make(time, 2, _ =>
        {
            Interlocked.Increment(ref checks);
            time.Advance(TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("The probe broke.");
        }, logger, new RecordingWarmupStep());

        // The health service reports a throwing check as Unhealthy, so the hold waits to its cap and then returns.
        await hold.StartAsync(CancellationToken.None).WaitAsync(Bound);

        Assert.Equal(2, checks);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task The_warm_up_begins_once_and_before_the_first_check()
    {
        var step = new RecordingWarmupStep();
        var checks = 0;
        var sawWarmupFirst = false;
        var hold = Make(new ManualTime(), 60, async ct =>
        {
            if (Interlocked.Increment(ref checks) == 1)
            {
                // The step runs only after Begin, so a first check that sees it run proves Begin came first.
                var first = await Task.WhenAny(step.Ran, Task.Delay(TimeSpan.FromSeconds(2), ct));
                sawWarmupFirst = first == step.Ran;
            }
            return HealthCheckResult.Healthy();
        }, new CapturingLogger(), step);

        await hold.StartAsync(CancellationToken.None).WaitAsync(Bound);

        Assert.True(sawWarmupFirst);
        Assert.Equal(1, step.Runs);
    }

    /// <summary>A check whose answer the test scripts.</summary>
    private sealed class ScriptedCheck : IHealthCheck
    {
        private readonly Func<CancellationToken, Task<HealthCheckResult>> _run;

        public ScriptedCheck(Func<CancellationToken, Task<HealthCheckResult>> run)
        {
            _run = run;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) => _run(cancellationToken);
    }

    /// <summary>A warm-up step that counts its runs and says when it first ran.</summary>
    private sealed class RecordingWarmupStep : IWarmupStep
    {
        private readonly TaskCompletionSource _ran = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _runs;

        public string Name => "recording";

        public int Runs => Volatile.Read(ref _runs);

        public Task Ran => _ran.Task;

        public Task RunAsync(IServiceProvider services, CancellationToken ct)
        {
            Interlocked.Increment(ref _runs);
            _ran.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>The host lifetime the warm-up reads. Nothing in these tests stops the host, so no token is ever cancelled.</summary>
    private sealed class TestLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
