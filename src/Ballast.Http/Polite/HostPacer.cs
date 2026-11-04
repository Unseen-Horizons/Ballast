// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;

namespace Ballast.Http;

/// <summary>
/// Spaces the requests to each host: a request starts at least its gap after the previous request to the same host ended. One
/// request per host runs at a time, so two callers cannot both start inside one gap. Hosts do not wait for each other. A request
/// must not start another paced request for the same host (the gate is not re-entrant), and the polite fetcher never does.
/// <para>
/// The host is the key, compared without case. To pace every request as one stream, pass the same key for all of them: each
/// request then waits for the previous one to end and its gap to pass, whatever the host.
/// </para>
/// <para>
/// Register one instance as a singleton, so the gap holds across callers. The clock is a <see cref="TimeProvider"/>, so a test
/// passes its own and nothing sleeps. The wait between requests is a real <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// </para>
/// </summary>
public sealed class HostPacer : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly ConcurrentDictionary<string, HostTurn> _turns = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    /// <summary>
    /// A pacer on the given clock. Null uses <see cref="TimeProvider.System"/>. The wait between two requests is a real delay.
    /// </summary>
    /// <param name="clock">The clock the gaps are measured on. Null uses the system clock.</param>
    public HostPacer(TimeProvider? clock = null)
        : this(clock ?? TimeProvider.System, (delay, ct) => Task.Delay(delay, ct))
    {
    }

    internal HostPacer(TimeProvider clock, Func<TimeSpan, CancellationToken, Task> wait)
    {
        _clock = clock;
        _wait = wait;
    }

    /// <summary>
    /// Runs <paramref name="request"/> once the host's gap since its previous request's end has passed, then records when this
    /// request ends. A request that fails still counts as ended, so the next one keeps its gap.
    /// </summary>
    /// <typeparam name="T">The result of the request.</typeparam>
    /// <param name="host">The host the request goes to, which is the pacing key. Required; compared without case.</param>
    /// <param name="gap">The least time from the end of one request to the start of the next to this host. Zero or more.</param>
    /// <param name="request">The request. Required. It must not start another request paced on the same host.</param>
    /// <param name="ct">Cancels the wait for the turn or for the gap. A request that has started is not cancelled here.</param>
    /// <returns>The result of <paramref name="request"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="host"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gap"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The pacer is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> is cancelled while waiting for the turn or the gap.</exception>
    public async Task<T> RunAsync<T>(string host, TimeSpan gap, Func<Task<T>> request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (gap < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gap), gap, "The gap is negative.");
        }

        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var turn = _turns.GetOrAdd(host, _ => new HostTurn());
        await turn.Gate.WaitAsync(ct);
        try
        {
            if (turn.LastEndedAt is { } ended)
            {
                var due = ended + gap - _clock.GetUtcNow();
                if (due > TimeSpan.Zero)
                {
                    await _wait(due, ct);
                }
            }

            try
            {
                return await request();
            }
            finally
            {
                turn.LastEndedAt = _clock.GetUtcNow();
            }
        }
        finally
        {
            turn.Gate.Release();
        }
    }

    /// <summary>
    /// Stops the pacer. A later <see cref="RunAsync{T}"/> throws <see cref="ObjectDisposedException"/>. A request already waiting
    /// or running finishes and keeps its gap. The per-host gates are not disposed under those requests, because releasing a
    /// disposed gate would throw from the request's own cleanup; they hold no unmanaged resource and are freed with the pacer.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _turns.Clear();
    }

    private sealed class HostTurn
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DateTimeOffset? LastEndedAt { get; set; }
    }
}
