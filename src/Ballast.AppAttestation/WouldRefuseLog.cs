// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace Ballast.AppAttestation;

/// <summary>
/// Logs, at most once a minute per route and caller, a call that would have been refused had enforcement been on. This is what the
/// owner reads in log-only mode to find old builds and browsers before switching <see cref="AppAttestationOptions.Enforce"/> on.
/// </summary>
public sealed class WouldRefuseLog
{
    private const int MaxKeys = 5_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastLogged = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly ILogger<WouldRefuseLog> _logger;

    /// <summary>Creates the log.</summary>
    /// <param name="clock">The clock the one-minute window is measured on.</param>
    /// <param name="logger">The logger the lines are written to.</param>
    public WouldRefuseLog(TimeProvider clock, ILogger<WouldRefuseLog> logger)
    {
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Writes a line for a call that would have been refused, unless the same caller was logged for this route in the last minute.</summary>
    /// <param name="route">The route, as the method and its pattern.</param>
    /// <param name="partition">The caller's rate-limit partition. It is hashed before it is logged.</param>
    /// <returns>True when a line was written (the first call for this key in the last minute).</returns>
    public bool Record(string route, string partition)
    {
        var now = _clock.GetUtcNow();

        // Hashed before it is remembered or logged: the partition can be a raw client address.
        var caller = AppSessionContext.LogLabel(partition);
        var key = $"{route}|{caller}";

        if (_lastLogged.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(1))
            return false;

        if (_lastLogged.Count >= MaxKeys)
        {
            foreach (var kv in _lastLogged)
            {
                if (now - kv.Value >= TimeSpan.FromMinutes(1))
                    _lastLogged.TryRemove(kv.Key, out _);
            }
        }

        // Past the cap with nothing stale: still log, just do not remember it. Better a repeated line than an unbounded dictionary.
        if (_lastLogged.Count < MaxKeys)
            _lastLogged[key] = now;

        _logger.LogInformation("App session would be required (log-only): {Route} from {Caller}", route, caller);
        return true;
    }
}
