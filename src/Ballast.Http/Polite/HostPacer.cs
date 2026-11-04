// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;

namespace Ballast.Http;

/// <summary>
/// Spaces the requests to each host: a request starts at least its gap after the previous request to the same host ended. One
/// request per host runs at a time, so two callers cannot both start inside one gap. Hosts do not wait for each other. A request
/// must not start another paced request for the same host (the gate is not re-entrant), and the fetcher never does.
/// </summary>
internal sealed class HostPacer
{
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly ConcurrentDictionary<string, HostTurn> _turns = new(StringComparer.OrdinalIgnoreCase);

    internal HostPacer(TimeProvider clock, Func<TimeSpan, CancellationToken, Task> wait)
    {
        _clock = clock;
        _wait = wait;
    }

    /// <summary>Runs <paramref name="request"/> once the host's gap since its previous request's end has passed, and records when it ends.</summary>
    public async Task<T> RunAsync<T>(string host, TimeSpan gap, Func<Task<T>> request, CancellationToken ct)
    {
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

    private sealed class HostTurn
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DateTimeOffset? LastEndedAt { get; set; }
    }
}
