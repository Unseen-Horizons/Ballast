// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Ballast.Auth;

/// <summary>How the last directory-sync run ended. <see cref="Off"/> is a run that did not read Microsoft because the sync is not set up.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MicrosoftSyncLastRun
{
    /// <summary>No run has been recorded since this process started.</summary>
    NotRun,

    /// <summary>The sync is not set up. Nothing was read, and the figures were cleared.</summary>
    Off,

    /// <summary>The run's changes were written.</summary>
    Applied,

    /// <summary>The run read Microsoft and applied nothing, because it would have switched off every linked account.</summary>
    NotApplied,

    /// <summary>The run could not finish. Nothing was applied, and the last applied figures are kept.</summary>
    Failed,
}

/// <summary>How near the client secret is to its end. Unknown until a run has read its end date.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClientSecretState
{
    /// <summary>No end date has been read.</summary>
    Unknown,

    /// <summary>The secret ends after the warning window.</summary>
    Ok,

    /// <summary>The secret ends within the warning window.</summary>
    Soon,

    /// <summary>The secret has ended.</summary>
    Expired,
}

/// <summary>
/// What the sync's last run shows, as it was at the time of the snapshot. Its lists are copies, so a later run does not change a snapshot
/// already taken.
/// </summary>
/// <param name="LastAttemptUtc">When the last run ended (applied, not applied or failed), or null before the first one since the process started.</param>
/// <param name="LastSuccessUtc">When the last applied run ended, or null when none has applied since the process started.</param>
/// <param name="LastRun">How the last run ended.</param>
/// <param name="Failure">Why the last run failed, for <see cref="MicrosoftSyncLastRun.Failed"/>; otherwise null.</param>
/// <param name="FailureStatus">The HTTP status Microsoft answered with, when the failure had one; otherwise null.</param>
/// <param name="Counts">The counts of the last run that read the directory, or zeros when none has (or the sync is off).</param>
/// <param name="Problems">What the owner should look at, from the last run that read the directory.</param>
/// <param name="RolesNotInMicrosoft">The roles that Microsoft no longer assigns, from the last run that read the directory. They are never deleted.</param>
/// <param name="ClientSecretEndsUtc">When the newest client secret ends, or null when none has been read.</param>
/// <param name="ClientSecretState">How near that end is, at the time of the snapshot.</param>
public sealed record MicrosoftSyncStatusSnapshot(
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    MicrosoftSyncLastRun LastRun,
    DirectorySyncFailure? Failure,
    int? FailureStatus,
    MicrosoftSyncCounts Counts,
    IReadOnlyList<MicrosoftSyncProblem> Problems,
    IReadOnlyList<string> RolesNotInMicrosoft,
    DateTimeOffset? ClientSecretEndsUtc,
    ClientSecretState ClientSecretState)
{
    /// <summary>True when the sync is switched off: the last run did not read Microsoft, and the figures were cleared.</summary>
    public bool IsOff => LastRun == MicrosoftSyncLastRun.Off;
}
