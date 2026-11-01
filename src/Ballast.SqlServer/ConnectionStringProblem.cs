// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.SqlServer;

/// <summary>What <see cref="ConnectionStringCheck.Classify"/> found in a connection string.</summary>
public enum ConnectionStringProblem
{
    /// <summary>The string names a server that is not this computer, and uses a login other than Windows authentication.</summary>
    None,

    /// <summary>The server is this computer: <c>localhost</c>, a loopback address (127.x.x.x or ::1), <c>.</c>, <c>(local)</c>, or the shared-memory protocol (<c>lpc:</c>).</summary>
    LocalHost,

    /// <summary>The server is a LocalDB instance, <c>(localdb)\...</c>.</summary>
    LocalDb,

    /// <summary>The string uses Windows authentication (<c>Trusted_Connection=True</c> or <c>Integrated Security=True</c>, or <c>SSPI</c>).</summary>
    IntegratedSecurity,

    /// <summary>
    /// The string is not a readable SQL Server connection string: it does not parse, or it names no server. It is refused, never
    /// guessed at.
    /// </summary>
    Unreadable,
}
