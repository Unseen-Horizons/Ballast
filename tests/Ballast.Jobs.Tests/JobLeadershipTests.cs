// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Ballast.Jobs.Tests;

/// <summary>
/// The job lock as one copy of an app sees it: how it takes the lock, what it does while it holds it, how it gives it
/// back. The lock itself is a scripted session, so nothing here needs a database.
/// </summary>
public class JobLeadershipTests
{
    [Fact]
    public async Task The_term_hooks_run_before_the_copy_is_the_holder()
    {
        JobLeadership? leadership = null;
        bool? holderDuringHook = null;
        var hook = new DelegateTermHook(_ =>
        {
            holderDuringHook = leadership?.IsHolder;
            return Task.CompletedTask;
        });
        await using var rig = new LeaderRig([hook]);
        leadership = rig.Leadership;
        rig.Sessions.Enqueue();

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder");

        Assert.False(holderDuringHook);
        Assert.Equal(JobLeadershipOutcome.Acquired, rig.Leadership.LastOutcome);
        Assert.NotNull(rig.Leadership.HolderSinceUtc);
    }

    [Fact]
    public async Task A_hook_that_throws_is_logged_and_the_term_goes_on()
    {
        var ran = 0;
        var failing = new DelegateTermHook(_ => throw new InvalidOperationException("the hook failed"));
        var second = new DelegateTermHook(_ =>
        {
            Interlocked.Increment(ref ran);
            return Task.CompletedTask;
        });
        await using var rig = new LeaderRig([failing, second]);
        rig.Sessions.Enqueue();

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder in spite of the failing hook");

        Assert.Equal(1, Volatile.Read(ref ran));
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("DelegateTermHook", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_copy_that_finds_the_lock_busy_keeps_asking_and_takes_it_when_it_is_free()
    {
        await using var rig = new LeaderRig();
        var busyOne = rig.Sessions.Enqueue();
        busyOne.Grants = false;
        var busyTwo = rig.Sessions.Enqueue();
        busyTwo.Grants = false;
        var free = rig.Sessions.Enqueue();

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder once the lock is free");

        Assert.Equal(3, rig.Sessions.CreatedCount);
        Assert.Equal(1, busyOne.AcquireCalls);
        Assert.Equal(1, busyTwo.AcquireCalls);
        Assert.True(busyOne.Disposed);
        Assert.True(busyTwo.Disposed);
        Assert.False(free.Disposed);
        Assert.Equal(JobLeadershipOutcome.Acquired, rig.Leadership.LastOutcome);
    }

    [Fact]
    public async Task A_busy_answer_is_not_an_error()
    {
        await using var rig = new LeaderRig(tune: o => o.BusyRetryDelay = TimeSpan.FromMinutes(5));
        rig.Sessions.Enqueue().Grants = false;

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.LastOutcome == JobLeadershipOutcome.HeldByOther, "the busy answer is recorded");

        Assert.False(rig.Leadership.IsHolder);
        Assert.Null(rig.Leadership.LastError);
        Assert.NotNull(rig.Leadership.LastAttemptUtc);
        Assert.Null(rig.Leadership.HolderSinceUtc);
    }

    [Fact]
    public async Task A_failed_attempt_is_recorded_and_waits_before_it_tries_again()
    {
        await using var rig = new LeaderRig(tune: o => o.ErrorRetryDelay = TimeSpan.FromMinutes(5));
        var broken = rig.Sessions.Enqueue();
        broken.OpenFails = new InvalidOperationException("database unreachable");

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.LastOutcome == JobLeadershipOutcome.Error, "the failure is recorded");

        Assert.Equal("database unreachable", rig.Leadership.LastError);
        Assert.False(rig.Leadership.IsHolder);

        // It backs off for the retry delay (five minutes here) rather than asking again at once.
        await Task.Delay(150);
        Assert.Equal(1, rig.Sessions.CreatedCount);
        Assert.True(broken.Disposed);
    }

    [Fact]
    public async Task After_a_failed_attempt_it_asks_again_and_can_become_the_holder()
    {
        await using var rig = new LeaderRig();
        rig.Sessions.Enqueue().AcquireFails = new InvalidOperationException("database unreachable");
        rig.Sessions.Enqueue();

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder on the second attempt");

        Assert.Equal(2, rig.Sessions.CreatedCount);
        Assert.Null(rig.Leadership.LastError);
        Assert.Equal(JobLeadershipOutcome.Acquired, rig.Leadership.LastOutcome);
    }

    [Fact]
    public async Task A_heartbeat_that_finds_the_lock_gone_ends_the_term_and_the_copy_asks_again()
    {
        await using var rig = new LeaderRig();
        var first = rig.Sessions.Enqueue();
        var second = rig.Sessions.Enqueue();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        second.OpenGate = gate;

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder");
        var term = await rig.Leadership.WaitForLeadershipAsync(CancellationToken.None);
        Assert.False(term.IsCancellationRequested);

        first.Holds = false;

        await JobWait.UntilAsync(() => term.IsCancellationRequested, "the term's token is cancelled");
        Assert.False(rig.Leadership.IsHolder);
        await JobWait.UntilAsync(() => rig.Leadership.LastOutcome == JobLeadershipOutcome.Lost, "the loss is recorded");
        Assert.NotNull(rig.Leadership.LastError);
        await JobWait.UntilAsync(() => first.Disposed, "the lost session is closed");
        Assert.Equal(0, first.ReleaseCalls);

        gate.SetResult();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy holds the lock again");

        var next = await rig.Leadership.WaitForLeadershipAsync(CancellationToken.None);
        Assert.NotEqual(term, next);
        Assert.False(next.IsCancellationRequested);
        Assert.Equal(JobLeadershipOutcome.Acquired, rig.Leadership.LastOutcome);
    }

    [Fact]
    public async Task A_heartbeat_that_cannot_ask_is_treated_as_a_lost_lock()
    {
        await using var rig = new LeaderRig();
        var first = rig.Sessions.Enqueue();
        var second = rig.Sessions.Enqueue();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        second.OpenGate = gate;

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder");
        var term = await rig.Leadership.WaitForLeadershipAsync(CancellationToken.None);

        first.HoldsFails = new InvalidOperationException("connection reset");

        await JobWait.UntilAsync(() => term.IsCancellationRequested, "the term's token is cancelled");
        await JobWait.UntilAsync(() => rig.Leadership.LastOutcome == JobLeadershipOutcome.Lost, "the loss is recorded");
        Assert.Equal("connection reset", rig.Leadership.LastError);
        Assert.False(rig.Leadership.IsHolder);

        gate.SetResult();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy holds the lock again");
    }

    [Fact]
    public async Task Stopping_the_application_ends_the_term_at_once_and_the_lock_is_released_only_when_the_service_stops()
    {
        await using var rig = new LeaderRig();
        var session = rig.Sessions.Enqueue();

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder");
        var term = await rig.Leadership.WaitForLeadershipAsync(CancellationToken.None);

        rig.Lifetime.StopApplication();

        // Immediately, not after the loop has noticed: every holder-only worker starts to wind down together.
        Assert.False(rig.Leadership.IsHolder);
        Assert.True(term.IsCancellationRequested);

        // The service's own loop has finished, and still the lock is held: the workers are not known to have stopped.
        await JobWait.UntilAsync(() => rig.Leadership.ExecuteTask?.IsCompleted == true, "the competing loop ends");
        Assert.Equal(0, session.ReleaseCalls);
        Assert.False(session.Disposed);

        await rig.Leadership.StopAsync(CancellationToken.None);

        Assert.Equal(1, session.ReleaseCalls);
        Assert.True(session.Disposed);
        Assert.Equal(JobLeadershipOutcome.Released, rig.Leadership.LastOutcome);
        var events = rig.Sessions.Events.ToArray();
        Assert.True(Array.IndexOf(events, "s1:release") < Array.IndexOf(events, "s1:dispose"), string.Join(", ", events));
    }

    [Fact]
    public async Task Stopping_the_service_without_the_application_stopping_first_still_releases_the_lock()
    {
        await using var rig = new LeaderRig();
        var session = rig.Sessions.Enqueue();

        await rig.StartAsync();
        await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder");

        await rig.Leadership.StopAsync(CancellationToken.None);

        Assert.False(rig.Leadership.IsHolder);
        Assert.Equal(1, session.ReleaseCalls);
        Assert.True(session.Disposed);
        Assert.Equal(JobLeadershipOutcome.Released, rig.Leadership.LastOutcome);
        Assert.Equal(1, rig.Sessions.CreatedCount);
    }

    [Fact]
    public async Task A_lock_that_is_granted_while_the_service_is_stopping_starts_no_hook_and_no_term()
    {
        var hookRuns = 0;
        var hook = new DelegateTermHook(_ =>
        {
            Interlocked.Increment(ref hookRuns);
            return Task.CompletedTask;
        });
        await using var rig = new LeaderRig([hook]);
        var session = rig.Sessions.Enqueue();
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AcquireGate = answer;
        session.AcquireIgnoresCancellation = true;

        await rig.StartAsync();
        await JobWait.UntilAsync(() => session.AcquireCalls == 1, "the attempt is waiting for the lock");

        // The host asks to stop, and only then does the server's answer arrive: the lock is granted.
        rig.Lifetime.StopApplication();
        answer.SetResult(true);
        await rig.Leadership.StopAsync(CancellationToken.None);

        Assert.Equal(0, Volatile.Read(ref hookRuns));
        Assert.False(rig.Leadership.IsHolder);
        Assert.Equal(1, session.ReleaseCalls);
        Assert.True(session.Disposed);
        Assert.Equal(1, rig.Sessions.CreatedCount);
    }

    [Fact]
    public async Task Whoever_waits_for_leadership_is_released_with_the_term_token_once_the_copy_holds_the_lock()
    {
        await using var rig = new LeaderRig();
        rig.Sessions.Enqueue();
        var waiting = rig.Leadership.WaitForLeadershipAsync(CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        await rig.StartAsync();
        var term = await waiting.WaitAsync(JobWait.Limit);

        Assert.False(term.IsCancellationRequested);
        Assert.True(rig.Leadership.IsHolder);
    }

    [Fact]
    public async Task Waiting_for_leadership_ends_when_the_caller_cancels()
    {
        await using var rig = new LeaderRig();
        using var cts = new CancellationTokenSource();
        var waiting = rig.Leadership.WaitForLeadershipAsync(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void With_the_lock_switched_off_this_process_is_always_the_holder()
    {
        var leadership = new AlwaysHolderLeadership();

        Assert.False(leadership.Enabled);
        Assert.True(leadership.IsHolder);
        Assert.Equal(JobLeadershipOutcome.Disabled, leadership.LastOutcome);
        Assert.Null(leadership.LastError);
        Assert.Null(leadership.LastAttemptUtc);
        Assert.NotNull(leadership.HolderSinceUtc);
    }

    [Fact]
    public async Task With_the_lock_switched_off_the_term_is_never_cancelled()
    {
        var leadership = new AlwaysHolderLeadership();

        var term = await leadership.WaitForLeadershipAsync(CancellationToken.None);

        Assert.False(term.CanBeCanceled);
    }

    [Fact]
    public async Task Work_that_finishes_on_its_own_is_not_run_again()
    {
        var leadership = new AlwaysHolderLeadership();
        var runs = 0;

        await leadership.RunAsLeaderAsync(
            _ =>
            {
                Interlocked.Increment(ref runs);
                return Task.CompletedTask;
            },
            once: false,
            CancellationToken.None);

        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Work_that_is_still_running_when_the_host_stops_ends_without_an_error()
    {
        var leadership = new AlwaysHolderLeadership();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var run = leadership.RunAsLeaderAsync(
            async ct =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
            once: false,
            cts.Token);
        await started.Task.WaitAsync(JobWait.Limit);

        await cts.CancelAsync();
        await run.WaitAsync(JobWait.Limit);

        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Work_inside_a_worker_runs_again_in_the_next_term_unless_it_is_once()
    {
        await using var rig = new LeaderRig();
        var first = rig.Sessions.Enqueue();
        rig.Sessions.Enqueue();
        var everyTerm = 0;
        var onceOnly = 0;
        using var hostStopping = new CancellationTokenSource();

        var every = rig.Leadership.RunAsLeaderAsync(
            async ct =>
            {
                Interlocked.Increment(ref everyTerm);
                await Task.Delay(Timeout.Infinite, ct);
            },
            once: false,
            hostStopping.Token);
        var single = rig.Leadership.RunAsLeaderAsync(
            async ct =>
            {
                Interlocked.Increment(ref onceOnly);
                await Task.Delay(Timeout.Infinite, ct);
            },
            once: true,
            hostStopping.Token);

        await rig.StartAsync();
        await JobWait.UntilAsync(() => Volatile.Read(ref everyTerm) == 1 && Volatile.Read(ref onceOnly) == 1, "both start in the first term");

        first.Holds = false;

        await JobWait.UntilAsync(() => Volatile.Read(ref everyTerm) == 2, "the repeating work starts again in the second term");
        await single.WaitAsync(JobWait.Limit);
        Assert.Equal(1, Volatile.Read(ref onceOnly));
        Assert.False(every.IsCompleted);

        await hostStopping.CancelAsync();
        await every.WaitAsync(JobWait.Limit);
    }

    [Fact]
    public async Task A_cancel_callback_that_blocks_holds_up_only_its_own_work_and_the_copy_takes_the_lock_again()
    {
        await using var rig = new LeaderRig();
        var first = rig.Sessions.Enqueue();
        rig.Sessions.Enqueue();
        var blockedRuns = 0;
        var otherRuns = 0;
        using var release = new ManualResetEventSlim();
        using var hostStopping = new CancellationTokenSource();
        try
        {
            await rig.StartAsync();
            await JobWait.UntilAsync(() => rig.Leadership.IsHolder, "the copy becomes the holder");

            // Each run polls its token, so it ends as soon as the token reads as cancelled, whatever its callbacks do.
            // The first run of this work leaves a callback on its token that blocks, as a SQL command's cancel can over a
            // failing connection (2026-10-29).
            var blocked = rig.Leadership.RunAsLeaderAsync(
                async ct =>
                {
                    if (Interlocked.Increment(ref blockedRuns) == 1)
                    {
                        _ = ct.Register(() => release.Wait());
                    }

                    while (!ct.IsCancellationRequested)
                    {
                        // The poll reads the token itself; the delay is not cancelled, so the run ends by returning.
                        await Task.Delay(5, CancellationToken.None);
                    }
                },
                once: false,
                hostStopping.Token);
            await JobWait.UntilAsync(() => Volatile.Read(ref blockedRuns) == 1, "the blocking work starts");

            // Started second, so its callback on the term is run first: it is cancelled before the other one blocks.
            var other = rig.Leadership.RunAsLeaderAsync(
                async ct =>
                {
                    Interlocked.Increment(ref otherRuns);
                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(5, CancellationToken.None);
                    }
                },
                once: false,
                hostStopping.Token);
            await JobWait.UntilAsync(() => Volatile.Read(ref otherRuns) == 1, "the other work starts");

            first.HoldsFails = new InvalidOperationException("Execution Timeout Expired");

            // Before the fix the heartbeat stayed inside the cancel: the lost term was never cleared or given up, and the
            // other work was handed its cancelled token again and again, at full CPU.
            await JobWait.UntilAsync(
                () => rig.Sessions.CreatedCount == 2 && rig.Leadership.IsHolder && Volatile.Read(ref otherRuns) >= 2,
                "the copy takes the lock again and the other work starts in the next term while the callback still blocks");
            await Task.Delay(100);
            Assert.Equal(2, Volatile.Read(ref otherRuns));
            Assert.Equal(1, Volatile.Read(ref blockedRuns));
            Assert.Contains(
                rig.Log.Entries,
                e => e.Level == LogLevel.Warning && e.Message.Contains("ended because the lock was lost", StringComparison.Ordinal));

            release.Set();
            await JobWait.UntilAsync(() => Volatile.Read(ref blockedRuns) == 2, "the blocked work starts again once its cancel is done");

            await hostStopping.CancelAsync();
            await blocked.WaitAsync(JobWait.Limit);
            await other.WaitAsync(JobWait.Limit);
        }
        finally
        {
            release.Set();
        }
    }
}
