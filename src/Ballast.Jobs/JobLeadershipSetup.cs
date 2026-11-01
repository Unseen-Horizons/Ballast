// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs;

/// <summary>
/// The values <see cref="JobLeadershipRegistration.AddJobLeadership"/> needs once, when the services are set up. They name
/// things (a configuration section, a connection's application name, a 409 text), so they are set in code, not bound from
/// configuration. The settings that are bound from configuration are <see cref="JobLeadershipOptions"/>.
/// </summary>
public sealed class JobLeadershipSetup
{
    /// <summary>The application name suffix used when none is given: <c>-lock</c>.</summary>
    public const string DefaultApplicationNameSuffix = "-lock";

    /// <summary>
    /// The configuration section that <see cref="JobLeadershipOptions"/> is bound from. The default is <c>JobLeadership</c>. It must
    /// not be blank.
    /// </summary>
    public string SettingsSection { get; set; } = JobLeadershipOptions.DefaultSectionName;

    /// <summary>
    /// The text added to the resource name to make the lock connection's application name, so a database administrator can see
    /// which session holds the lock. The default is <c>-lock</c>. It may be empty, but not null.
    /// </summary>
    public string ApplicationNameSuffix { get; set; } = DefaultApplicationNameSuffix;

    /// <summary>The words of the 409 answer that a copy without the lock gives. Its title and detail must not be blank.</summary>
    public RequireJobHolderOptions RequireJobHolder { get; } = new();
}
