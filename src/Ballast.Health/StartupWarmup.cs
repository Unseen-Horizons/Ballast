// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Health;

/// <summary>One piece of work a copy does before its warm-up gate opens (fill a cache, load a table). It runs in a scope of its own.</summary>
public interface IWarmupStep
{
    /// <summary>What the log calls this step.</summary>
    string Name { get; }

    /// <summary>Does the work. It may throw: <see cref="StartupWarmup"/> logs the failure and goes on.</summary>
    /// <param name="services">The scope's services.</param>
    /// <param name="ct">Cancelled when the host stops.</param>
    Task RunAsync(IServiceProvider services, CancellationToken ct);
}

/// <summary>
/// Runs the warm-up steps in the background, so the copy is not held up. Readiness says when they are done (a warm-up gate,
/// <see cref="HealthRegistration.AddWarmupGate"/>). It is not a hosted service. The startup hold starts it when that is on, and
/// otherwise the application's started event does (<see cref="HealthEndpoints.MapReadiness"/> registers that), so the
/// hosted-service order of the app is unchanged. Each step is timed and logged. A step that fails is logged and the copy is ready
/// anyway, because a cold cache is slow, not wrong.
/// </summary>
public sealed class StartupWarmup
{
    private readonly IReadOnlyList<IWarmupStep> _steps;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger _logger;
    private readonly BuildInfo _build;
    private readonly HealthStart _start;
    private int _begun;
    private volatile bool _complete;

    /// <summary>Creates the warm-up runner.</summary>
    /// <param name="steps">The steps, in the order they run. Required; may be empty.</param>
    /// <param name="scopes">Creates a scope for each step. Required.</param>
    /// <param name="lifetime">The host's lifetime, so the steps stop when the host does. Required.</param>
    /// <param name="logger">Where the step lines go. Required.</param>
    /// <param name="build">The build identity, logged once at the start. Required.</param>
    /// <param name="start">When the process started. Required.</param>
    public StartupWarmup(
        IEnumerable<IWarmupStep> steps,
        IServiceScopeFactory scopes,
        IHostApplicationLifetime lifetime,
        ILogger logger,
        BuildInfo build,
        HealthStart start)
    {
        _steps = steps.ToList();
        _scopes = scopes;
        _lifetime = lifetime;
        _logger = logger;
        _build = build;
        _start = start;
    }

    /// <summary>True once every step has finished (or the host stopped first).</summary>
    public bool IsComplete => _complete;

    /// <summary>Starts the steps on a background thread, and logs the build once. A second call does nothing.</summary>
    public void Begin()
    {
        if (Interlocked.Exchange(ref _begun, 1) == 1)
        {
            return;
        }

        _logger.LogInformation(
            "Build: commit {Commit}, built {BuiltAtUtc}; started {StartedUtc}",
            _build.Commit ?? "none", _build.BuiltAtUtc ?? "none", _start.StartedUtc);

        _ = Task.Run(() => RunAsync(_lifetime.ApplicationStopping));
    }

    private async Task RunAsync(CancellationToken stopping)
    {
        var clock = Stopwatch.StartNew();
        foreach (var step in _steps)
        {
            if (stopping.IsCancellationRequested)
            {
                break;
            }

            var stepClock = Stopwatch.StartNew();
            try
            {
                using var scope = _scopes.CreateScope();
                await step.RunAsync(scope.ServiceProvider, stopping);
                _logger.LogInformation("Warm-up step {Step} finished in {Seconds} s", step.Name, Seconds(stepClock));
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Warm-up step {Step} failed after {Seconds} s; the copy is ready anyway", step.Name, Seconds(stepClock));
            }
        }

        _complete = true;
        _logger.LogInformation("Warm-up finished in {Seconds} s", Seconds(clock));
    }

    private static int Seconds(Stopwatch clock) => (int)clock.Elapsed.TotalSeconds;
}
