// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Jobs.Tests;

/// <summary>
/// A worker behind the job lock without a line of the worker changed: it runs only while this copy holds the lock, is told
/// to stop when the term ends, and is started again, on the same instance, in the next term.
/// </summary>
public class LeaderHostedServiceTests
{
    /// <summary>A <see cref="BackgroundService"/> that counts its starts and how many runs overlap.</summary>
    private sealed class ProbeWorker : BackgroundService
    {
        private int _starts;
        private int _running;
        private int _maxRunning;

        public int Starts => Volatile.Read(ref _starts);

        public int Running => Volatile.Read(ref _running);

        public int MaxRunning => Volatile.Read(ref _maxRunning);

        /// <summary>What a run does. Null waits for the token and returns.</summary>
        public Func<CancellationToken, Task>? Body { get; set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref _starts);
            var running = Interlocked.Increment(ref _running);
            int seen;
            do
            {
                seen = Volatile.Read(ref _maxRunning);
            }
            while (running > seen && Interlocked.CompareExchange(ref _maxRunning, running, seen) != seen);

            try
            {
                if (Body is { } body)
                {
                    await body(stoppingToken);
                }
                else
                {
                    await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private static LeaderHostedService<ProbeWorker> Wrap(
        ProbeWorker worker, IJobLeadership leadership, bool once = false, TimeSpan? budget = null, JobTestLogger<LeaderHostedService<ProbeWorker>>? logger = null) =>
        new(worker, leadership, once, budget ?? TimeSpan.FromSeconds(5), logger ?? new JobTestLogger<LeaderHostedService<ProbeWorker>>());

    [Fact]
    public async Task The_worker_does_not_start_before_this_copy_holds_the_lock()
    {
        await using var rig = new LeaderRig();
        var session = rig.Sessions.Enqueue();
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AcquireGate = answer;
        var worker = new ProbeWorker();
        using var service = Wrap(worker, rig.Leadership);

        await rig.StartAsync();
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        Assert.Equal(0, worker.Starts);
        Assert.False(rig.Leadership.IsHolder);

        answer.SetResult(true);
        await JobWait.UntilAsync(() => worker.Starts == 1, "the worker starts once the copy holds the lock");
        Assert.True(rig.Leadership.IsHolder);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, worker.Running);
    }

    [Fact]
    public async Task The_worker_is_stopped_when_the_lock_is_lost_and_started_again_in_the_next_term()
    {
        await using var rig = new LeaderRig();
        var first = rig.Sessions.Enqueue();
        rig.Sessions.Enqueue();
        var worker = new ProbeWorker();
        using var service = Wrap(worker, rig.Leadership);

        await rig.StartAsync();
        await service.StartAsync(CancellationToken.None);
        await JobWait.UntilAsync(() => worker.Starts == 1, "the worker starts in the first term");

        first.Holds = false;

        await JobWait.UntilAsync(() => worker.Starts == 2, "the same worker is started again in the next term");
        Assert.Equal(1, worker.MaxRunning);
        Assert.True(rig.Leadership.IsHolder);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, worker.Running);
    }

    [Fact]
    public async Task No_new_term_starts_while_the_old_run_has_not_finished()
    {
        await using var rig = new LeaderRig();
        var first = rig.Sessions.Enqueue();
        rig.Sessions.Enqueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new ProbeWorker { Body = async _ => await release.Task };
        var logger = new JobTestLogger<LeaderHostedService<ProbeWorker>>();
        using var service = Wrap(worker, rig.Leadership, budget: TimeSpan.FromMilliseconds(50), logger: logger);

        await rig.StartAsync();
        await service.StartAsync(CancellationToken.None);
        await JobWait.UntilAsync(() => worker.Starts == 1, "the worker starts in the first term");

        first.Holds = false;

        await JobWait.UntilAsync(() => rig.Sessions.CreatedCount == 2 && rig.Leadership.IsHolder, "the copy holds the lock again");
        await Task.Delay(200);
        Assert.Equal(1, worker.Starts);
        Assert.Equal(1, worker.Running);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("did not stop within", StringComparison.Ordinal));

        release.SetResult();

        await JobWait.UntilAsync(() => worker.Starts == 2, "the worker starts in the second term once the first run has ended");
        Assert.Equal(1, worker.MaxRunning);
    }

    [Fact]
    public async Task With_once_the_worker_is_not_started_in_a_second_term()
    {
        await using var rig = new LeaderRig();
        var first = rig.Sessions.Enqueue();
        rig.Sessions.Enqueue();
        var worker = new ProbeWorker();
        using var service = Wrap(worker, rig.Leadership, once: true);

        await rig.StartAsync();
        await service.StartAsync(CancellationToken.None);
        await JobWait.UntilAsync(() => worker.Starts == 1, "the worker starts in the first term");

        first.Holds = false;

        await JobWait.UntilAsync(() => service.ExecuteTask?.IsCompleted == true, "the wrapper ends after the worker's one term");
        await JobWait.UntilAsync(() => rig.Sessions.CreatedCount == 2 && rig.Leadership.IsHolder, "the copy holds the lock again");
        await Task.Delay(150);
        Assert.Equal(1, worker.Starts);
        Assert.Equal(0, worker.Running);
    }

    [Fact]
    public async Task A_worker_that_finishes_on_its_own_is_not_started_again()
    {
        await using var rig = new LeaderRig();
        rig.Sessions.Enqueue();
        var worker = new ProbeWorker { Body = _ => Task.CompletedTask };
        using var service = Wrap(worker, rig.Leadership);

        await rig.StartAsync();
        await service.StartAsync(CancellationToken.None);
        await JobWait.UntilAsync(() => service.ExecuteTask?.IsCompleted == true, "the wrapper ends with the worker");
        await Task.Delay(150);

        Assert.Equal(1, worker.Starts);
        Assert.True(rig.Leadership.IsHolder);
    }

    [Fact]
    public async Task A_fault_in_the_worker_is_rethrown_as_the_wrappers_own()
    {
        await using var rig = new LeaderRig();
        rig.Sessions.Enqueue();
        var worker = new ProbeWorker
        {
            Body = async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("the worker failed");
            },
        };
        using var service = Wrap(worker, rig.Leadership);

        await rig.StartAsync();
        await service.StartAsync(CancellationToken.None);
        await JobWait.UntilAsync(() => service.ExecuteTask?.IsCompleted == true, "the wrapper ends with the worker's fault");

        var task = service.ExecuteTask;
        Assert.NotNull(task);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("the worker failed", failure.Message);
    }

    [Fact]
    public async Task With_the_lock_switched_off_the_worker_runs_at_once_and_stops_with_the_host()
    {
        var worker = new ProbeWorker();
        using var service = Wrap(worker, new AlwaysHolderLeadership());

        await service.StartAsync(CancellationToken.None);
        await JobWait.UntilAsync(() => worker.Starts == 1, "the worker starts without waiting for a lock");

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, worker.Running);
        Assert.Equal(1, worker.Starts);
        Assert.True(service.ExecuteTask?.IsCompleted);
    }

    [Fact]
    public async Task A_stopping_copys_worker_unwinds_before_the_lock_is_released()
    {
        await using var rig = new LeaderRig();
        var session = rig.Sessions.Enqueue();
        var worker = new ProbeWorker
        {
            Body = async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                // Winding down takes a moment, as a real worker's final flush does. The flush is not cancelled by the term.
                await Task.Delay(100, CancellationToken.None);
                rig.Sessions.Events.Enqueue("worker:stopped");
            },
        };
        using var service = Wrap(worker, rig.Leadership);

        await rig.StartAsync();
        await service.StartAsync(CancellationToken.None);
        await JobWait.UntilAsync(() => worker.Starts == 1, "the worker starts");

        // The host's order: the application starts stopping, the other services stop, and the lock service stops last.
        rig.Lifetime.StopApplication();
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, session.ReleaseCalls);
        await rig.Leadership.StopAsync(CancellationToken.None);

        Assert.Equal(1, session.ReleaseCalls);
        var events = rig.Sessions.Events.ToArray();
        Assert.True(Array.IndexOf(events, "worker:stopped") >= 0, string.Join(", ", events));
        Assert.True(Array.IndexOf(events, "worker:stopped") < Array.IndexOf(events, "s1:release"), string.Join(", ", events));
    }
}
