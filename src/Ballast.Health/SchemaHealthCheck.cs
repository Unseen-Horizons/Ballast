// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Health;

/// <summary>What the database's migration history says, and what this copy's code ships with.</summary>
/// <param name="Code">Every migration id the code ships with, in order.</param>
/// <param name="Applied">Every migration id the database has applied.</param>
public sealed record MigrationReadout(IReadOnlyCollection<string> Code, IReadOnlyCollection<string> Applied);

/// <summary>Where the schema check reads its two lists. Production reads EF; a test supplies them.</summary>
public interface IMigrationSource
{
    /// <summary>Reads the code's migration ids and the database's applied ids.</summary>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>Both lists.</returns>
    Task<MigrationReadout> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// The production source: the code's migrations and the history table, read in a fresh scope so that the check, which is a
/// singleton, never holds a context.
/// </summary>
/// <typeparam name="TContext">The application's DbContext.</typeparam>
public sealed class EfMigrationSource<TContext> : IMigrationSource where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Creates the source over the scope factory.</summary>
    /// <param name="scopes">Creates a scope, and so a context, for each read.</param>
    public EfMigrationSource(IServiceScopeFactory scopes) { _scopes = scopes; }

    /// <inheritdoc />
    public async Task<MigrationReadout> ReadAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>().Database;
        var code = database.GetMigrations().ToList();
        var applied = await database.GetAppliedMigrationsAsync(ct);
        return new MigrationReadout(code, applied.ToList());
    }
}

/// <summary>
/// The <c>schema</c> readiness check. Migrations the code ships with that the database has not applied make the copy
/// Unhealthy, and they are checked again every <c>SchemaRecheckSeconds</c>. Once none are pending the schema is current for
/// good, and the database is never asked again. A database ahead of the code (extra applied ids, after a rollback) is still
/// Healthy, because the code needs only what it ships with.
/// </summary>
public sealed class SchemaHealthCheck : IHealthCheck, IDisposable
{
    private readonly IMigrationSource _source;
    private readonly IOptions<HealthOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SchemaHealthCheck> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _current;
    private HealthCheckResult? _last;
    private DateTimeOffset _lastCheckedUtc;

    /// <summary>Creates the check.</summary>
    /// <param name="source">Reads the two migration lists. Required.</param>
    /// <param name="options">The readiness settings. Required.</param>
    /// <param name="time">The clock for the recheck interval. Required.</param>
    /// <param name="logger">Where a failure's type is written. Required.</param>
    public SchemaHealthCheck(IMigrationSource source, IOptions<HealthOptions> options, TimeProvider time, ILogger<SchemaHealthCheck> logger)
    {
        _source = source;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (_current)
        {
            return HealthCheckResult.Healthy("The database schema is current.");
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_current)
            {
                return HealthCheckResult.Healthy("The database schema is current.");
            }

            if (_last is not null && _time.GetUtcNow() - _lastCheckedUtc < TimeSpan.FromSeconds(_options.Value.SchemaRecheckSeconds))
            {
                return _last.Value;
            }

            var result = await ReadResultAsync(ct);
            _last = result;
            _lastCheckedUtc = _time.GetUtcNow();
            if (result.Status == HealthStatus.Healthy)
            {
                _current = true;
            }
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HealthCheckResult> ReadResultAsync(CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.Value.CheckTimeoutSeconds)));
        try
        {
            var readout = await _source.ReadAsync(limit.Token);
            var applied = readout.Applied.ToHashSet(StringComparer.Ordinal);
            var pending = readout.Code.Count(id => !applied.Contains(id));
            return pending == 0
                ? HealthCheckResult.Healthy("The database schema is current.")
                : HealthCheckResult.Unhealthy($"{pending} migration(s) are not applied yet.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The type says what went wrong without the server name or connection string a message can carry.
            _logger.LogWarning("Schema check could not read the migration history: {Failure}", ex.GetType().Name);
            return HealthCheckResult.Unhealthy("The migration history could not be read.");
        }
    }

    /// <summary>Releases the gate.</summary>
    public void Dispose() => _gate.Dispose();
}
