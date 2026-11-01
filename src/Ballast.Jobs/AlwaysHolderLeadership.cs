// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs;

/// <summary>
/// Used when <see cref="JobLeadershipOptions.Enabled"/> is false, which is the default: no lock is taken and this process is
/// always the holder, so Development and the tests behave as they did before the lock existed.
/// </summary>
public sealed class AlwaysHolderLeadership : IJobLeadership
{
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    /// <summary>Always false: the lock is off.</summary>
    public bool Enabled => false;

    /// <summary>Always true: this process is the holder.</summary>
    public bool IsHolder => true;

    /// <summary>The start of the process, since the holder never changes.</summary>
    public DateTime? HolderSinceUtc => _startedUtc;

    /// <summary>Always null: no lock is tried.</summary>
    public DateTime? LastAttemptUtc => null;

    /// <summary>Always <see cref="JobLeadershipOutcome.Disabled"/>.</summary>
    public JobLeadershipOutcome LastOutcome => JobLeadershipOutcome.Disabled;

    /// <summary>Always null.</summary>
    public string? LastError => null;

    /// <summary>When this process began.</summary>
    public DateTime StartedUtc => _startedUtc;

    /// <summary>A term that is never cancelled: the host's own stopping token ends the work.</summary>
    /// <param name="ct">Not used: the term is never cancelled.</param>
    public Task<CancellationToken> WaitForLeadershipAsync(CancellationToken ct) => Task.FromResult(CancellationToken.None);

    /// <summary>Runs the work with the host's token, once, or again whenever it ends early and <paramref name="once"/> is false.</summary>
    /// <param name="work">The work to run.</param>
    /// <param name="once">True to run the work at most once.</param>
    /// <param name="ct">The host's stopping token.</param>
    public Task RunAsLeaderAsync(Func<CancellationToken, Task> work, bool once, CancellationToken ct) =>
        JobLeadershipLoop.RunAsync(this, work, once, ct);
}
