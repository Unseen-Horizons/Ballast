// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs;

/// <summary>What the last attempt to take (or keep) the job lock came to.</summary>
public enum JobLeadershipOutcome
{
    /// <summary>No attempt has been made yet: the service has not started.</summary>
    NotStarted,

    /// <summary>The lock is switched off, so this process is always the holder.</summary>
    Disabled,

    /// <summary>This copy holds the lock.</summary>
    Acquired,

    /// <summary>Another copy holds the lock. Not a fault: it is what a deploy looks like from the new copy.</summary>
    HeldByOther,

    /// <summary>The attempt failed (the database could not be reached, for example).</summary>
    Error,

    /// <summary>This copy held the lock and found it gone.</summary>
    Lost,

    /// <summary>This copy let go of the lock at shutdown.</summary>
    Released,
}

/// <summary>
/// Which copy of an app runs the background work that writes. Exactly one copy is the holder at a time;
/// the others keep serving requests and keep their in-memory caches filling, and take over when the
/// holder lets go.
/// </summary>
public interface IJobLeadership
{
    /// <summary>False when the lock is switched off and this process is always the holder.</summary>
    bool Enabled { get; }

    /// <summary>True while this copy may run holder-only work. Cleared at once when the application starts stopping.</summary>
    bool IsHolder { get; }

    /// <summary>When the current term began, or null when this copy is not the holder.</summary>
    DateTime? HolderSinceUtc { get; }

    /// <summary>When the lock was last tried, or null before the first attempt.</summary>
    DateTime? LastAttemptUtc { get; }

    /// <summary>What the last attempt to take (or keep) the lock came to.</summary>
    JobLeadershipOutcome LastOutcome { get; }

    /// <summary>The message of the last error or loss, or null.</summary>
    string? LastError { get; }

    /// <summary>When this process began (event time).</summary>
    DateTime StartedUtc { get; }

    /// <summary>
    /// Completes when this copy is the holder, with a token that is cancelled when its term ends (the lock
    /// was lost, or the application is stopping). Completes at once when it is the holder already.
    /// </summary>
    Task<CancellationToken> WaitForLeadershipAsync(CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="work"/> while this copy is the holder: it waits for leadership, runs the work with
    /// the term's token, and when the term ends before the work does, waits for the next term and runs it again.
    /// Used inside a worker that must run on every copy but writes in one part of it.
    /// <para>
    /// A piece of work that finishes on its own is not run again. With <paramref name="once"/> the work is run in
    /// at most one term, even when that term ended before it finished.
    /// </para>
    /// </summary>
    Task RunAsLeaderAsync(Func<CancellationToken, Task> work, bool once, CancellationToken ct);
}

/// <summary>The one loop behind <see cref="IJobLeadership.RunAsLeaderAsync"/>, shared by both implementations.</summary>
internal static class JobLeadershipLoop
{
    internal static async Task RunAsync(IJobLeadership leadership, Func<CancellationToken, Task> work, bool once, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            CancellationToken term;
            try
            {
                term = await leadership.WaitForLeadershipAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(term, ct);
            try
            {
                await work(linked.Token);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                // The term ended, or the host is stopping, while the work ran. Anything else propagates.
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            // The work ended on its own while the term was still running: it is finished, not interrupted.
            if (!term.IsCancellationRequested)
            {
                return;
            }

            if (once)
            {
                return;
            }
        }
    }
}
