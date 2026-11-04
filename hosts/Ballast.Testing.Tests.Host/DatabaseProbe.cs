// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Hosting;

namespace Ballast.Testing.Tests.Host;

/// <summary>What the probe reads and writes: whether the test database is still there, and a record of what happened.</summary>
public interface IDatabaseState
{
    /// <summary>True once the database has been dropped.</summary>
    bool Dropped { get; }

    /// <summary>Records one entry, in order.</summary>
    /// <param name="entry">The entry to record.</param>
    void Record(string entry);
}

/// <summary>A hosted service that touches the database when the host stops, as a real one can. It fails when the database is gone.</summary>
public sealed class DatabaseProbe : IHostedService
{
    private readonly IDatabaseState _state;

    /// <summary>Creates the probe.</summary>
    /// <param name="state">The database state it reads.</param>
    public DatabaseProbe(IDatabaseState state)
    {
        _state = state;
    }

    /// <summary>Does nothing when the host starts.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Uses the database while the host stops. It throws if the database was already dropped.</summary>
    /// <param name="cancellationToken">Cancels the stop.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_state.Dropped)
        {
            _state.Record("stop: database already gone");
            throw new InvalidOperationException("The database was dropped before the host stopped.");
        }

        _state.Record("stop: database still there");
        return Task.CompletedTask;
    }
}
