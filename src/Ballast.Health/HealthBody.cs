// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Health;

/// <summary>
/// When this process started (event time, UTC). The readiness caps are measured from it, and the build body reports it.
/// </summary>
/// <param name="StartedUtc">The start instant, in UTC.</param>
public sealed record HealthStart(DateTime StartedUtc);

/// <summary>
/// The body of the build route (<c>/health</c> by default). It says what this copy is and when it started, so a deploy can
/// read the commit to see the switch. <c>Commit</c> and <c>BuiltAtUtc</c> are null when the build did not record them.
/// </summary>
/// <param name="Status">Always <c>healthy</c>: the route answers only while the process runs.</param>
/// <param name="Timestamp">The instant of the answer, in UTC.</param>
/// <param name="Commit">The commit the build came from, or null.</param>
/// <param name="BuiltAtUtc">The build time, in UTC, or null.</param>
/// <param name="StartedUtc">The instant this process started, in UTC.</param>
public sealed record HealthBody(string Status, DateTime Timestamp, string? Commit, string? BuiltAtUtc, DateTime StartedUtc)
{
    /// <summary>Builds the body from the build info read at start, the start time and the current UTC time.</summary>
    /// <param name="build">What this copy was built from.</param>
    /// <param name="startedUtc">When the process started, in UTC.</param>
    /// <param name="utcNow">The instant of the answer, in UTC.</param>
    public static HealthBody Create(BuildInfo build, DateTime startedUtc, DateTime utcNow) =>
        new("healthy", utcNow, build.Commit, build.BuiltAtUtc, startedUtc);
}
