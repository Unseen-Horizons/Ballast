// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;

using Ballast.Maui.Helpers;

namespace Ballast.Maui.Tests;

/// <summary>
/// The debounce: a burst of calls runs only its last action, after the pause, and a cancelled or disposed one never runs. Short delays keep the tests fast; the waits are generous multiples of them.
/// </summary>
public sealed class DebouncerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan Long = TimeSpan.FromMilliseconds(600);

    [Fact]
    public async Task Only_the_last_call_in_a_burst_runs()
    {
        var ran = new List<int>();
        using var debouncer = new Debouncer(Delay);

        debouncer.Call(() => ran.Add(1));
        debouncer.Call(() => ran.Add(2));
        debouncer.Call(() => ran.Add(3));
        await Task.Delay(Long);

        Assert.Equal([3], ran);
    }

    [Fact]
    public async Task The_action_waits_for_the_delay()
    {
        var ran = false;
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(250));

        debouncer.Call(() => ran = true);
        await Task.Delay(40);
        Assert.False(ran);

        await Task.Delay(Long);
        Assert.True(ran);
    }

    [Fact]
    public async Task A_new_call_restarts_the_wait()
    {
        var ran = 0;
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(500));

        debouncer.Call(() => ran++);
        await Task.Delay(200);
        debouncer.Call(() => ran++);
        await Task.Delay(200);

        // About 400 ms in: the first wait would end at 500 ms and the second at about 700 ms, so neither has ended.
        Assert.Equal(0, ran);

        // About 1000 ms in: the second wait ended once, at about 700 ms. Without the restart, the first wait would also have ended, and this would be 2.
        await Task.Delay(Long);
        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task Cancel_drops_the_waiting_action()
    {
        var ran = false;
        using var debouncer = new Debouncer(Delay);

        debouncer.Call(() => ran = true);
        debouncer.Cancel();
        await Task.Delay(Long);

        Assert.False(ran);
    }

    [Fact]
    public async Task Dispose_drops_the_waiting_action()
    {
        var ran = false;
        var debouncer = new Debouncer(Delay);

        debouncer.Call(() => ran = true);
        debouncer.Dispose();
        await Task.Delay(Long);

        Assert.False(ran);
    }

    [Fact]
    public async Task It_can_be_used_again_after_a_cancel()
    {
        var ran = new List<string>();
        using var debouncer = new Debouncer(Delay);

        debouncer.Call(() => ran.Add("dropped"));
        debouncer.Cancel();
        debouncer.Call(() => ran.Add("kept"));
        await Task.Delay(Long);

        Assert.Equal(["kept"], ran);
    }

    [Fact]
    public async Task A_throwing_action_does_not_break_the_next_call()
    {
        var ran = false;
        using var debouncer = new Debouncer(Delay);

        debouncer.Call(() => throw new InvalidOperationException("boom"));
        await Task.Delay(Long);
        debouncer.Call(() => ran = true);
        await Task.Delay(Long);

        Assert.True(ran);
    }

    [Fact]
    public async Task The_action_resumes_on_the_callers_synchronization_context()
    {
        // A view model calls from the UI thread and the action touches bound collections, so it must come back to the caller's context.
        var context = new SingleThreadContext();
        try
        {
            var ranOn = new TaskCompletionSource<SynchronizationContext?>();
            using var debouncer = new Debouncer(Delay);

            context.Post(() => debouncer.Call(() => ranOn.TrySetResult(SynchronizationContext.Current)));

            var seen = await ranOn.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(context, seen);
        }
        finally
        {
            context.Stop();
        }
    }

    /// <summary>A minimal single-threaded context: everything posted to it runs on its one thread.</summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public SingleThreadContext()
        {
            var thread = new Thread(Run) { IsBackground = true };
            thread.Start();
        }

        public void Post(Action action) => Post(_ => action(), null);

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void Stop() => _queue.CompleteAdding();

        private void Run()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }
    }
}
