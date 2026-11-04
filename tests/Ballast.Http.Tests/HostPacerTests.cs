// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http.Tests;

/// <summary>
/// The per-host pacer. The clock and the wait are injected, so a test moves time forward instead of sleeping. The public
/// constructor is also checked on its own clock, where no wait is needed.
/// </summary>
public class HostPacerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(3);

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (HostPacer Pacer, ManualClock Clock, List<TimeSpan> Waits) Make()
    {
        var clock = new ManualClock(Start);
        var waits = new List<TimeSpan>();
        var pacer = new HostPacer(clock, (delay, _) =>
        {
            waits.Add(delay);
            clock.Now += delay;
            return Task.CompletedTask;
        });
        return (pacer, clock, waits);
    }

    [Fact]
    public async Task The_first_request_to_a_host_does_not_wait()
    {
        var (pacer, _, waits) = Make();

        Assert.Equal(1, await pacer.RunAsync("a.example", Gap, () => Task.FromResult(1)));

        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_second_request_to_the_same_host_waits_the_full_gap()
    {
        var (pacer, clock, waits) = Make();
        await pacer.RunAsync("a.example", Gap, () => Task.FromResult(1));

        DateTimeOffset startedSecond = default;
        await pacer.RunAsync("a.example", Gap, () =>
        {
            startedSecond = clock.Now;
            return Task.FromResult(2);
        });

        Assert.Equal([Gap], waits);
        Assert.Equal(Start + Gap, startedSecond);
    }

    [Fact]
    public async Task The_gap_is_counted_from_the_end_of_a_slow_request_not_its_start()
    {
        var (pacer, clock, waits) = Make();
        await pacer.RunAsync("a.example", Gap, async () =>
        {
            clock.Now += TimeSpan.FromSeconds(5);
            return await Task.FromResult(1);
        });

        DateTimeOffset startedSecond = default;
        await pacer.RunAsync("a.example", Gap, () =>
        {
            startedSecond = clock.Now;
            return Task.FromResult(2);
        });

        Assert.Equal([Gap], waits);
        Assert.Equal(Start + TimeSpan.FromSeconds(8), startedSecond);
    }

    [Fact]
    public async Task A_request_that_came_after_the_gap_has_passed_does_not_wait()
    {
        var (pacer, clock, waits) = Make();
        await pacer.RunAsync("a.example", Gap, () => Task.FromResult(1));
        clock.Now += TimeSpan.FromSeconds(10);

        await pacer.RunAsync("a.example", Gap, () => Task.FromResult(2));

        Assert.Empty(waits);
    }

    [Fact]
    public async Task Different_hosts_do_not_wait_for_each_other()
    {
        var (pacer, _, waits) = Make();
        await pacer.RunAsync("a.example", Gap, () => Task.FromResult(1));

        await pacer.RunAsync("b.example", Gap, () => Task.FromResult(2));

        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_host_name_is_compared_without_case()
    {
        var (pacer, _, waits) = Make();
        await pacer.RunAsync("A.Example", Gap, () => Task.FromResult(1));

        await pacer.RunAsync("a.example", Gap, () => Task.FromResult(2));

        Assert.Equal([Gap], waits);
    }

    [Fact]
    public async Task A_zero_gap_never_waits()
    {
        var (pacer, _, waits) = Make();

        for (var i = 0; i < 3; i++)
        {
            await pacer.RunAsync("a.example", TimeSpan.Zero, () => Task.FromResult(i));
        }

        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_request_that_fails_still_counts_as_ended_for_the_next_one()
    {
        var (pacer, _, waits) = Make();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pacer.RunAsync<int>("a.example", Gap, () => Task.FromException<int>(new InvalidOperationException("no page"))));
        await pacer.RunAsync("a.example", Gap, () => Task.FromResult(2));

        Assert.Equal([Gap], waits);
    }

    [Fact]
    public async Task Requests_to_one_host_run_one_at_a_time()
    {
        var (pacer, _, _) = Make();
        var running = 0;
        var most = 0;

        var tasks = Enumerable.Range(0, 5).Select(_ => pacer.RunAsync("a.example", TimeSpan.Zero, async () =>
        {
            most = Math.Max(most, Interlocked.Increment(ref running));
            await Task.Yield();
            Interlocked.Decrement(ref running);
            return 0;
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, most);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_request_before_it_starts()
    {
        var (pacer, _, _) = Make();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var started = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pacer.RunAsync("a.example", Gap, () =>
        {
            started = true;
            return Task.FromResult(1);
        }, cts.Token));

        Assert.False(started);
    }

    [Fact]
    public async Task A_wait_cancelled_during_the_gap_throws_and_the_request_does_not_run()
    {
        var clock = new ManualClock(Start);
        var pacer = new HostPacer(clock, (delay, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        await pacer.RunAsync("a.example", Gap, () => Task.FromResult(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var started = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pacer.RunAsync("a.example", Gap, () =>
        {
            started = true;
            return Task.FromResult(2);
        }, cts.Token));

        Assert.False(started);
    }

    [Fact]
    public async Task The_public_constructor_measures_the_gap_on_the_given_clock()
    {
        var clock = new ManualClock(Start);
        using var pacer = new HostPacer(clock);
        await pacer.RunAsync("a.example", Gap, () => Task.FromResult(1));
        clock.Now += Gap;

        // The gap has passed on the given clock, so the request runs at once and never reaches the real delay.
        Assert.Equal(2, await pacer.RunAsync("a.example", Gap, () => Task.FromResult(2)));
    }

    [Fact]
    public async Task The_public_constructor_without_a_clock_runs_requests_on_the_system_clock()
    {
        using var pacer = new HostPacer();

        Assert.Equal(7, await pacer.RunAsync("a.example", TimeSpan.Zero, () => Task.FromResult(7)));
    }

    [Fact]
    public async Task Invalid_arguments_are_refused()
    {
        var (pacer, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() => pacer.RunAsync(" ", Gap, () => Task.FromResult(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pacer.RunAsync("a.example", TimeSpan.FromSeconds(-1), () => Task.FromResult(1)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => pacer.RunAsync<int>("a.example", Gap, null!));
    }

    [Fact]
    public async Task A_disposed_pacer_refuses_new_requests()
    {
        var (pacer, _, _) = Make();
        pacer.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pacer.RunAsync("a.example", Gap, () => Task.FromResult(1)));
    }

    [Fact]
    public async Task A_request_running_when_the_pacer_is_disposed_finishes_normally()
    {
        var (pacer, _, _) = Make();
        var release = new TaskCompletionSource();
        var running = pacer.RunAsync("a.example", Gap, async () =>
        {
            await release.Task;
            return 5;
        });

        pacer.Dispose();
        release.SetResult();

        Assert.Equal(5, await running);
    }
}
