// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The Microsoft directory sync's last run, in memory: one per process. Only the job holder runs the sync, so this is the holder's record;
/// a copy that is not the holder has no run to show, and after a restart it reads not-run until the next run. An app registers it as a
/// singleton and records each run's <see cref="MicrosoftSyncResult"/> into it. The app's admin page maps <see cref="Snapshot()"/> to its own
/// JSON.
/// <para>
/// Everything is guarded by one lock, so a run and a read may happen on different threads. A run is recorded as one change: a reader never
/// sees a run's outcome without its figures. Lists are replaced, never edited in place. <see cref="Gate"/> is for the caller: it serialises
/// runs, and this class does not take it.
/// </para>
/// </summary>
public sealed class MicrosoftSyncStatus
{
    private readonly object _lock = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _secretWarningWindow;

    private DateTimeOffset? _lastAttemptUtc;
    private DateTimeOffset? _lastSuccessUtc;
    private MicrosoftSyncLastRun _lastRun = MicrosoftSyncLastRun.NotRun;
    private DirectorySyncFailure? _failure;
    private int? _failureStatus;
    private MicrosoftSyncCounts _counts = new(0, 0, 0, 0, 0);
    private MicrosoftSyncProblem[] _problems = [];
    private string[] _rolesNotInMicrosoft = [];
    private DateTimeOffset? _secretEndsUtc;

    /// <summary>Creates the status.</summary>
    /// <param name="options">The settings. Null for the defaults.</param>
    /// <param name="time">The clock <see cref="Snapshot()"/> reads. Null for the system clock.</param>
    /// <exception cref="InvalidOperationException">The options are out of range. See <see cref="MicrosoftSyncStatusOptions.Validate"/>.</exception>
    public MicrosoftSyncStatus(MicrosoftSyncStatusOptions? options = null, TimeProvider? time = null)
    {
        options ??= new MicrosoftSyncStatusOptions();
        options.Validate();
        _secretWarningWindow = options.SecretWarningWindow;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// One run at a time. A caller takes it before a run and releases it after. This class does not take it itself, so a caller that
    /// records a run without holding it is not blocked.
    /// </summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>When the last run ended (applied, not applied or failed). Null before the first one since the process started.</summary>
    public DateTimeOffset? LastAttemptUtc
    {
        get
        {
            lock (_lock)
            {
                return _lastAttemptUtc;
            }
        }
    }

    /// <summary>
    /// Records what a library sync run came to. A run on a copy that does not hold the job lock read nothing, so it records nothing.
    /// </summary>
    /// <param name="run">The run's result, as <see cref="MicrosoftDirectorySync{TUser,TKey,TRole,TRefresh,TContext}.RunOnceAsync"/> returns it.</param>
    /// <param name="atUtc">When the run ended, as a UTC instant.</param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A run that failed names no failure. The library always names one.</exception>
    public void Record(MicrosoftSyncResult run, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(run);

        switch (run.Outcome)
        {
            case MicrosoftSyncOutcome.Off:
                RecordOff();
                break;
            case MicrosoftSyncOutcome.NotHolder:
                break;
            case MicrosoftSyncOutcome.ReadFailed:
            case MicrosoftSyncOutcome.WriteFailed:
                RecordFailed(atUtc, FailureOf(run), run.HttpStatus, run.ClientSecretEndsUtc);
                break;
            case MicrosoftSyncOutcome.LooksWrong:
                RecordFigures(MicrosoftSyncLastRun.NotApplied, atUtc, run, applied: false);
                break;
            case MicrosoftSyncOutcome.Applied:
                RecordFigures(MicrosoftSyncLastRun.Applied, atUtc, run, applied: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(run), run.Outcome, "A sync outcome this status does not record.");
        }
    }

    /// <summary>
    /// Records a run that failed in the app itself, outside the library: an exception after the read, such as the database, with nothing
    /// applied. The failure is <see cref="DirectorySyncFailure.Unexpected"/>, with no HTTP status.
    /// </summary>
    /// <param name="atUtc">When the run ended, as a UTC instant.</param>
    public void RecordUnexpectedFailure(DateTimeOffset atUtc)
    {
        RecordFailed(atUtc, DirectorySyncFailure.Unexpected, null, null);
    }

    /// <summary>
    /// Records that the sync is switched off. Nothing is read, so the last run's figures, problems and roles are cleared, and none of them
    /// reads as current. The last attempt time and the client secret's end are kept.
    /// </summary>
    public void RecordOff()
    {
        lock (_lock)
        {
            _lastRun = MicrosoftSyncLastRun.Off;
            _failure = null;
            _failureStatus = null;
            _counts = new MicrosoftSyncCounts(0, 0, 0, 0, 0);
            _problems = [];
            _rolesNotInMicrosoft = [];
        }
    }

    /// <summary>
    /// Whether the last run that read the directory listed <paramref name="roleName"/> as no longer in Microsoft. The comparison ignores
    /// case. False before any such run, and after the sync is switched off.
    /// </summary>
    /// <param name="roleName">The role's name.</param>
    /// <returns>True when the role is listed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roleName"/> is null.</exception>
    public bool IsNotInMicrosoft(string roleName)
    {
        ArgumentNullException.ThrowIfNull(roleName);

        lock (_lock)
        {
            return Array.Exists(_rolesNotInMicrosoft, role => string.Equals(role, roleName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The status as it is now, by the clock the status was created with.</summary>
    /// <returns>The snapshot.</returns>
    public MicrosoftSyncStatusSnapshot Snapshot() => Snapshot(_time.GetUtcNow());

    /// <summary>The status as it was at <paramref name="now"/>. The client secret's state is worked out at that instant.</summary>
    /// <param name="now">The instant the state is judged at, as a UTC instant.</param>
    /// <returns>The snapshot.</returns>
    public MicrosoftSyncStatusSnapshot Snapshot(DateTimeOffset now)
    {
        lock (_lock)
        {
            return new MicrosoftSyncStatusSnapshot(
                _lastAttemptUtc,
                _lastSuccessUtc,
                _lastRun,
                _failure,
                _failureStatus,
                _counts,
                _problems,
                _rolesNotInMicrosoft,
                _secretEndsUtc,
                SecretStateOf(_secretEndsUtc, now));
        }
    }

    /// <summary>
    /// How near the end of the client secret is at <paramref name="now"/>. An end at or before <paramref name="now"/> is expired. An end
    /// within the warning window is soon. A null end is unknown.
    /// </summary>
    /// <param name="endsUtc">When the secret ends, as a UTC instant, or null when unknown.</param>
    /// <param name="now">The instant the state is judged at.</param>
    /// <returns>The state.</returns>
    public ClientSecretState SecretStateOf(DateTimeOffset? endsUtc, DateTimeOffset now) =>
        endsUtc is not { } ends ? ClientSecretState.Unknown
        : ends <= now ? ClientSecretState.Expired
        : ends - now <= _secretWarningWindow ? ClientSecretState.Soon
        : ClientSecretState.Ok;

    // A run that could not finish. Nothing was applied, and the last applied figures are kept as they were.
    private void RecordFailed(DateTimeOffset atUtc, DirectorySyncFailure failure, int? httpStatus, DateTimeOffset? secretEndsUtc)
    {
        lock (_lock)
        {
            _lastAttemptUtc = atUtc;
            _lastRun = MicrosoftSyncLastRun.Failed;
            _failure = failure;
            _failureStatus = httpStatus;
            _secretEndsUtc = secretEndsUtc ?? _secretEndsUtc;
        }
    }

    // A run that read the directory: its figures, problems and roles are shown whether it applied or not. Only an applied run is a success.
    private void RecordFigures(MicrosoftSyncLastRun lastRun, DateTimeOffset atUtc, MicrosoftSyncResult run, bool applied)
    {
        lock (_lock)
        {
            _lastAttemptUtc = atUtc;
            _lastRun = lastRun;
            _failure = null;
            _failureStatus = null;
            if (applied)
            {
                _lastSuccessUtc = atUtc;
            }

            if (run.Counts is { } counts)
            {
                _counts = counts;
            }

            _problems = [.. run.Problems];
            _rolesNotInMicrosoft = [.. run.RolesNotInMicrosoft];
            _secretEndsUtc = run.ClientSecretEndsUtc ?? _secretEndsUtc;
        }
    }

    private static DirectorySyncFailure FailureOf(MicrosoftSyncResult run) =>
        run.Failure ?? throw new InvalidOperationException("A failed directory sync run names no failure.");
}
