// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Testing.Sql;

/// <summary>
/// The settings of the SQL Server test databases: the environment variable that names the server, the server used when that
/// variable is blank, the marker that every database a run makes carries in its name, and how old a leftover must be before a
/// later run drops it. Set once with <see cref="TestDatabase.Configure"/>, before the first database is named.
/// </summary>
public sealed class TestDatabaseOptions
{
    /// <summary>The default environment variable: <c>BALLAST_TEST_SQL</c>.</summary>
    public const string DefaultConnectionVariable = "BALLAST_TEST_SQL";

    /// <summary>A trusted connection to the SQL Server on this machine. The default when nothing else is configured.</summary>
    public const string DefaultLocalConnection = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>The default name marker: <c>_btt</c> (Ballast test).</summary>
    public const string DefaultNameMarker = "_btt";

    /// <summary>The default leftover age: 24 hours.</summary>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(24);

    /// <summary>
    /// The environment variable that holds the base connection string. Any database name in it is replaced. A test project
    /// that already uses another name sets it here. Default <c>BALLAST_TEST_SQL</c>.
    /// </summary>
    public string ConnectionVariable { get; set; } = DefaultConnectionVariable;

    /// <summary>The base connection string used when <see cref="ConnectionVariable"/> is unset or blank. Default: the local server.</summary>
    public string DefaultConnection { get; set; } = DefaultLocalConnection;

    /// <summary>
    /// The marker between a database's purpose and its run id: an underscore and 1 to 16 letters, such as <c>_btt</c>. A sweep
    /// and a drop refuse any name without it. Default <c>_btt</c>.
    /// </summary>
    public string NameMarker { get; set; } = DefaultNameMarker;

    /// <summary>
    /// A marked database made by another run is a leftover, and is dropped before the first test, once it is at least this old by
    /// the server's clock. A live run's databases are never that old. Default 24 hours. Must be above zero.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = DefaultStaleAfter;
}
