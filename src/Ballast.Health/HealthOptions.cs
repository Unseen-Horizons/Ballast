// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

namespace Ballast.Health;

/// <summary>
/// The readiness settings, bound from the configuration section named by <see cref="HealthSetupOptions.SettingsSection"/>
/// (<c>Health</c> unless the app says otherwise). The defaults are the ones the zero-downtime deploy was measured against. A
/// copy is switched to only once its checks say it can serve, and a capped startup gate says "ready anyway" after its cap
/// instead of holding a deploy forever. The caps of the startup gates are not here: each gate is registered with its own cap
/// (<see cref="HealthRegistration.AddStartupGate"/>).
/// </summary>
public sealed class HealthOptions
{
    /// <summary>The configuration section the settings are bound from when the setup does not name one: <c>Health</c>.</summary>
    public const string DefaultSectionName = "Health";

    /// <summary>How long one database answer is reused. Probes inside that window make no database call.</summary>
    public int DatabaseCacheSeconds { get; set; } = 15;

    /// <summary>How long the database may stop answering, after it has answered, before the copy is Unhealthy.</summary>
    public int DatabaseGraceSeconds { get; set; } = 90;

    /// <summary>How often a pending schema is checked again. A schema that is current is never checked again.</summary>
    public int SchemaRecheckSeconds { get; set; } = 5;

    /// <summary>The limit on one database or schema probe, so a hung connection cannot hold a probe open. A value below 1 is read as 1.</summary>
    public int CheckTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// The longest a new copy keeps its port closed while its readiness checks are not yet passing, measured from process
    /// start (<see cref="StartupHold"/>). Zero, or less, turns the hold off, so development and the tests start at once.
    /// </summary>
    public int StartupHoldMaxSeconds { get; set; }
}

/// <summary>
/// The values <see cref="HealthRegistration.AddBallastHealth"/> needs once, when the services are set up. They name things
/// (a configuration section, a metadata key, a log category), so they are set in code, not bound from configuration.
/// </summary>
public sealed class HealthSetupOptions
{
    /// <summary>The log category the readiness lines are written under when none is given: <c>Ballast.Health</c>.</summary>
    public const string DefaultLogCategory = "Ballast.Health";

    /// <summary>
    /// The configuration section that <see cref="HealthOptions"/> binds from. The default is <c>Health</c>. It must not be
    /// blank.
    /// </summary>
    public string SettingsSection { get; set; } = HealthOptions.DefaultSectionName;

    /// <summary>
    /// The assembly metadata key the build writes its build time under. The default is <c>BuiltAtUtc</c>. It must not be
    /// blank.
    /// </summary>
    public string BuiltAtMetadataKey { get; set; } = BuildInfoReader.DefaultBuiltAtKey;

    /// <summary>
    /// The category the readiness log lines are written under. The default is <c>Ballast.Health</c>. An app sets its own
    /// namespace, so the lines show in the platform's log stream with the app's other lines. It must not be blank.
    /// </summary>
    public string LogCategory { get; set; } = DefaultLogCategory;

    /// <summary>
    /// The assembly whose metadata gives the build identity. Null means the entry assembly, which is the application. A test
    /// host may set another assembly here.
    /// </summary>
    public Assembly? BuildAssembly { get; set; }
}
