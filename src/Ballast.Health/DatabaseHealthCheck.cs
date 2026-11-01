// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Health;

/// <summary>Whether the database answers a connection right now. Production asks a DbContext; a test answers itself.</summary>
public interface IDatabaseReachability
{
    /// <summary>Asks whether a connection to the database can be opened.</summary>
    /// <param name="ct">Cancels the ask.</param>
    /// <returns>True when the database answers.</returns>
    Task<bool> CanConnectAsync(CancellationToken ct = default);
}

/// <summary>The production answer: <c>DatabaseFacade.CanConnectAsync</c> on a context from a fresh scope.</summary>
/// <typeparam name="TContext">The application's DbContext.</typeparam>
public sealed class EfDatabaseReachability<TContext> : IDatabaseReachability where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Creates the reachability answer over the scope factory.</summary>
    /// <param name="scopes">Creates a scope, and so a context, for each ask.</param>
    public EfDatabaseReachability(IServiceScopeFactory scopes) { _scopes = scopes; }

    /// <inheritdoc />
    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TContext>().Database.CanConnectAsync(ct);
    }
}

/// <summary>
/// The <c>database</c> readiness check. It asks the database at most once every <c>DatabaseCacheSeconds</c>, however many
/// probes come in: one ask runs at a time and the rest wait for its answer. Each ask is held to <c>CheckTimeoutSeconds</c>,
/// because a failed connect can be held by the retry policy for about 30 s.
/// <para>
/// A copy that has never reached the database is Unhealthy at once. A copy that has is Degraded, and still ready, for
/// <c>DatabaseGraceSeconds</c> after the database stops answering, and Unhealthy after that. This is the one upstream
/// problem that can make a ready copy unready.
/// </para>
/// <para>
/// No exception text reaches the result or the response. The type of a failure is logged.
/// </para>
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck, IDisposable
{
    private readonly IDatabaseReachability _reachability;
    private readonly IOptions<HealthOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<DatabaseHealthCheck> _logger;
    private readonly SemaphoreSlim _probe = new(1, 1);
    private DateTimeOffset? _lastAskedUtc;
    private bool _lastReachable;
    private bool _everReachable;
    private DateTimeOffset? _lostSinceUtc;

    /// <summary>Creates the check.</summary>
    /// <param name="reachability">Answers whether the database is reachable. Required.</param>
    /// <param name="options">The readiness settings. Required.</param>
    /// <param name="time">The clock for the cache and the grace. Required.</param>
    /// <param name="logger">Where a failure's type is written. Required.</param>
    public DatabaseHealthCheck(IDatabaseReachability reachability, IOptions<HealthOptions> options, TimeProvider time, ILogger<DatabaseHealthCheck> logger)
    {
        _reachability = reachability;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        await _probe.WaitAsync(ct);
        try
        {
            var options = _options.Value;
            var now = _time.GetUtcNow();
            if (_lastAskedUtc is null || now - _lastAskedUtc.Value >= TimeSpan.FromSeconds(options.DatabaseCacheSeconds))
            {
                var reachable = await AskAsync(ct);
                now = _time.GetUtcNow();
                _lastAskedUtc = now;
                _lastReachable = reachable;
                if (reachable)
                {
                    _everReachable = true;
                    _lostSinceUtc = null;
                }
                else if (_everReachable)
                {
                    _lostSinceUtc ??= now;
                }
            }

            return Describe(now, options);
        }
        finally
        {
            _probe.Release();
        }
    }

    private async Task<bool> AskAsync(CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.Value.CheckTimeoutSeconds)));
        try
        {
            return await _reachability.CanConnectAsync(limit.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The type says what went wrong without the server name or connection string a message can carry.
            _logger.LogWarning("Database check could not reach the database: {Failure}", ex.GetType().Name);
            return false;
        }
    }

    private HealthCheckResult Describe(DateTimeOffset now, HealthOptions options)
    {
        if (!_everReachable)
        {
            return HealthCheckResult.Unhealthy("The database has not answered since this copy started.");
        }

        if (_lastReachable)
        {
            return HealthCheckResult.Healthy("The database answers.");
        }

        var lostFor = now - (_lostSinceUtc ?? now);
        var seconds = (int)lostFor.TotalSeconds;
        return lostFor.TotalSeconds < options.DatabaseGraceSeconds
            ? HealthCheckResult.Degraded($"The database has not answered for {seconds} s; this copy stays ready for {options.DatabaseGraceSeconds} s.")
            : HealthCheckResult.Unhealthy($"The database has not answered for {seconds} s.");
    }

    /// <summary>Releases the probe gate.</summary>
    public void Dispose() => _probe.Dispose();
}
