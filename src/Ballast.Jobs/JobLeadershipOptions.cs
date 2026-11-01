// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs;

/// <summary>
/// The settings of the background-job lock, bound from the configuration section named by
/// <see cref="JobLeadershipSetup.SettingsSection"/> (<c>JobLeadership</c> unless the app says otherwise).
/// <para>
/// During a deploy the old and the new copy of an app run side by side for a while. Background work that writes
/// must run in one copy at a time, so the copies compete for one SQL Server application lock and only the holder runs it.
/// <see cref="Enabled"/> is off by default: Development and the test host behave as before (this process is always the
/// holder), and Production turns it on.
/// </para>
/// </summary>
public sealed class JobLeadershipOptions
{
    /// <summary>The configuration section the settings are bound from when the setup does not name one: <c>JobLeadership</c>.</summary>
    public const string DefaultSectionName = "JobLeadership";

    /// <summary>False (the default): this process is always the holder and no lock is taken.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The application-lock resource both copies of one app compete for. Empty (the default) means the resource the app passed
    /// to <see cref="JobLeadershipRegistration.AddJobLeadership"/>. A value here overrides that resource.
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>How long one <c>sp_getapplock</c> call waits for the holder to let go. SQL grants the lock the moment it is released.</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How often the holder checks it still holds the lock.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The wait before trying again after another copy was found holding the lock.</summary>
    public TimeSpan BusyRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The wait before trying again after an error (the database was unreachable, for example).</summary>
    public TimeSpan ErrorRetryDelay { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long the release at shutdown may take. Closing the connection frees the lock either way.</summary>
    public TimeSpan ReleaseTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
