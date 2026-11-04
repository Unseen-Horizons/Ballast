// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Jobs;

/// <summary>
/// Runs a worker that writes only while this copy holds the job lock, without a line changed in the worker.
/// <para>
/// It waits for leadership, starts the inner worker with the term's token as its stopping token, and waits for
/// the worker to finish. When the term ends the worker is told to stop and gets <see cref="StopBudget"/> to
/// unwind; no new term starts until its task has really finished, so two runs of one worker never overlap. A
/// worker that stopped because the term ended is started again, on the same instance, in the next term. A worker
/// that finishes on its own (it is switched off, or it is a one-shot) is not started again. A fault in the worker
/// is rethrown, so the host treats it exactly as it would have if the worker had been registered directly.
/// </para>
/// </summary>
/// <typeparam name="T">The worker. It has to be able to start again after it stopped, which a loop in <c>ExecuteAsync</c> is.</typeparam>
public sealed class LeaderHostedService<T> : BackgroundService where T : BackgroundService
{
    /// <summary>How long a worker gets to unwind after its term ends, when the registration does not say otherwise: 45 seconds.</summary>
    public static readonly TimeSpan DefaultStopBudget = TimeSpan.FromSeconds(45);

    private readonly T _inner;
    private readonly IJobLeadership _leadership;
    private readonly bool _once;
    private readonly ILogger<LeaderHostedService<T>> _logger;

    /// <summary>Wraps a worker. <see cref="JobLeadershipRegistration.AddLeaderHostedService{T}"/> is the normal way to make one.</summary>
    /// <param name="inner">The worker to run.</param>
    /// <param name="leadership">The job lock.</param>
    /// <param name="once">True to run the worker in at most one term.</param>
    /// <param name="stopBudget">How long the worker gets to unwind when its term ends.</param>
    /// <param name="logger">Receives the line written when a worker does not stop in time.</param>
    public LeaderHostedService(T inner, IJobLeadership leadership, bool once, TimeSpan stopBudget, ILogger<LeaderHostedService<T>> logger)
    {
        _inner = inner;
        _leadership = leadership;
        _once = once;
        StopBudget = stopBudget;
        _logger = logger;
    }

    /// <summary>How long the worker gets to unwind after its term ends.</summary>
    public TimeSpan StopBudget { get; }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _leadership.RunAsLeaderAsync(RunTermAsync, _once, stoppingToken);

    /// <summary>One term of the worker: from its start until its task has finished.</summary>
    private async Task RunTermAsync(CancellationToken term)
    {
        // A worker whose ExecuteAsync throws before its first await surfaces here, as it would from the host.
        await _inner.StartAsync(term);

        var executeTask = _inner.ExecuteTask;
        if (executeTask is null)
        {
            return;
        }

        // Until the worker ends by itself or the term ends. Cancelling the term cancels the worker's own token too.
        await Task.WhenAny(executeTask, Task.Delay(Timeout.Infinite, term));

        if (!executeTask.IsCompleted)
        {
            using var budget = new CancellationTokenSource(StopBudget);
            await _inner.StopAsync(budget.Token);
            if (!executeTask.IsCompleted)
            {
                _logger.LogError(
                    "{Worker} did not stop within {Budget}; it is not started again until it has",
                    typeof(T).Name, StopBudget);
                await executeTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        // Cancellation is the term ending, not a fault. A real exception is rethrown as the worker's own.
        if (executeTask.IsFaulted)
        {
            await executeTask;
        }
    }
}
