// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace Ballast.Maui.Helpers;

/// <summary>
/// Runs an action once the calls stop: each <see cref="Call"/> restarts the wait, and only the last action in a burst runs. Used for search boxes,
/// where filtering a list on each keystroke rebuilds it for one word.
/// </summary>
/// <remarks>
/// The action runs on the context that called <see cref="Call"/>, because the wait resumes through the captured synchronisation context (there is
/// deliberately no <c>ConfigureAwait(false)</c> here). A view model calls it from the UI thread and the action touches bound collections, so it
/// must come back to that thread. Pure and platform-free, so a test can drive it.
/// </remarks>
public sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _pending;

    /// <summary>Creates a debouncer with a fixed wait.</summary>
    /// <param name="delay">How long the calls must stop before the action runs. Zero or more.</param>
    public Debouncer(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "The delay must not be negative.");
        _delay = delay;
    }

    /// <summary>Schedules <paramref name="action"/>, replacing any action still waiting.</summary>
    /// <param name="action">The action to run once the calls stop.</param>
    public void Call(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var next = new CancellationTokenSource();
        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = next;
        }

        _ = RunAsync(action, next.Token);
    }

    /// <summary>Drops the action still waiting, if any.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
        }
    }

    /// <summary>Drops the action still waiting. Safe to call more than once.</summary>
    public void Dispose() => Cancel();

    private async Task RunAsync(Action action, CancellationToken ct)
    {
        try
        {
            await Task.Delay(_delay, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Superseded or cancelled in the instant the wait finished.
        if (ct.IsCancellationRequested)
            return;

        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Nobody awaits this task, so an exception would vanish; say so instead.
            Trace.WriteLine($"[Debouncer] The debounced action failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
