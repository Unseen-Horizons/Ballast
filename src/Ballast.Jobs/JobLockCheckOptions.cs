// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs;

/// <summary>
/// The settings of the job-lock readiness check, bound from the configuration section the app names in
/// <see cref="JobLeadershipRegistration.AddJobLockReadiness"/>. The key is <c>JobLockGraceMinutes</c>, so an app that keeps its
/// readiness settings in a <c>Health</c> section binds <c>Health:JobLockGraceMinutes</c>.
/// </summary>
public sealed class JobLockCheckOptions
{
    /// <summary>
    /// How long a copy may fail to hold or wait for the job lock before its readiness is Unhealthy. The default is 15 minutes.
    /// Zero, or less, makes the check Unhealthy as soon as the copy is in a bad state.
    /// </summary>
    public int JobLockGraceMinutes { get; set; } = 15;
}
