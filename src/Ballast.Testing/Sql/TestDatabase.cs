// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

namespace Ballast.Testing.Sql;

/// <summary>
/// Names, creates and drops the SQL Server databases that a test run uses, and says whether the server can be reached.
/// <para>
/// Several test runs can share one SQL Server at the same time: two sessions on a developer machine, or a run beside a build. A
/// fixed database name would then be shared too, and one run's drop would end another run's database. So every database a run
/// creates is named by <see cref="NewName"/>: <c>{purpose}{marker}{RunId}_{n}</c>. The marker says which kind of run made it, and the
/// run id says which run. <see cref="ConnectionStringFor"/>, <see cref="CreateEmptyAsync"/> and <see cref="DropAsync"/> refuse any
/// other name, so a fixed name fails on the first test that uses it.
/// </para>
/// <para>
/// The settings come from <see cref="TestDatabaseOptions"/>. <see cref="Configure"/> must run before the first database is named,
/// and only once; after that the settings are fixed, so two parts of one run never disagree about the server or the marker.
/// </para>
/// </summary>
public static class TestDatabase
{
    private const string PurposeBody = "[A-Za-z][A-Za-z0-9]{0,63}";

    private static readonly object Gate = new();
    private static readonly Regex PurposePattern = new("^" + PurposeBody + @"\z", RegexOptions.CultureInvariant);
    private static readonly Regex CollationPattern = new(@"^[A-Za-z][A-Za-z0-9_]{0,127}\z", RegexOptions.CultureInvariant);
    private static readonly Regex MarkerPattern = new(@"^_[A-Za-z]{1,16}\z", RegexOptions.CultureInvariant);

    private static Settings? _settings;
    private static bool _fixed;
    private static int _counter;

    /// <summary>12 lowercase hex characters, one per test process. Two runs on one server never share it.</summary>
    public static readonly string RunId = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Sets the settings. Must run before the first database is named or the server is read, and only once. A value that cannot
    /// work is refused here, at once, rather than as a failed connection later.
    /// </summary>
    /// <param name="options">The settings to use. Required.</param>
    /// <exception cref="ArgumentException">A setting is blank, the marker is not an underscore and letters, or the age is not above zero.</exception>
    /// <exception cref="InvalidOperationException">The settings are already fixed by an earlier use.</exception>
    public static void Configure(TestDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionVariable))
            throw new ArgumentException("The connection variable's name is blank.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.DefaultConnection))
            throw new ArgumentException("The default connection string is blank.", nameof(options));
        if (options.NameMarker is null || !MarkerPattern.IsMatch(options.NameMarker))
            throw new ArgumentException("The name marker is an underscore and 1 to 16 letters, such as _btt.", nameof(options));
        if (options.StaleAfter <= TimeSpan.Zero)
            throw new ArgumentException("The leftover age must be above zero.", nameof(options));

        lock (Gate)
        {
            if (_fixed)
                throw new InvalidOperationException(
                    "TestDatabase.Configure must run before the first test database is named or the server is read, and only once. "
                    + "The settings are already fixed.");
            _settings = new Settings(options);
        }
    }

    /// <summary>The marker in use. Fixes the settings.</summary>
    public static string NameMarker => Fixed().Marker;

    /// <summary>The leftover age in use. Fixes the settings.</summary>
    internal static TimeSpan StaleAfter => Fixed().StaleAfter;

    /// <summary>The connection string to the <c>master</c> database, for creating and dropping databases. Fixes the settings.</summary>
    public static string Master => WithCatalog("master");

    /// <summary>
    /// The base connection string to the server, without a database: the variable's value, or the default when the variable is blank.
    /// Fixes the settings.
    /// </summary>
    public static string Server => Fixed().BaseConnection;

    /// <summary>
    /// A database name for this run. <paramref name="purpose"/> is letters and digits, starting with a letter, at most 64 characters;
    /// it is what a reader sees in the server's database list.
    /// </summary>
    /// <param name="purpose">What the database is for, such as <c>Locks</c>.</param>
    /// <exception cref="ArgumentException">The purpose is not letters and digits, starting with a letter.</exception>
    public static string NewName(string purpose)
    {
        if (purpose is null || !PurposePattern.IsMatch(purpose))
            throw new ArgumentException(
                "A test database purpose is letters and digits, starting with a letter, at most 64 characters.",
                nameof(purpose));

        var settings = Fixed();
        var n = Interlocked.Increment(ref _counter);
        return $"{purpose}{settings.Marker}{RunId}_{n}";
    }

    /// <summary>
    /// The run id of a database a test run made (any run), or <c>null</c> when the name is not one of those names. It does not say
    /// whether the database still exists.
    /// </summary>
    /// <param name="databaseName">The database name to read.</param>
    internal static string? RunIdOf(string databaseName)
    {
        if (databaseName is null) return null;
        var match = Fixed().RunName.Match(databaseName);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>True when <paramref name="databaseName"/> is a database this run created with <see cref="NewName"/>.</summary>
    /// <param name="databaseName">The database name to check.</param>
    internal static bool IsOwnName(string databaseName) => RunIdOf(databaseName) == RunId;

    /// <summary>Builds a connection string for a database this run created with <see cref="NewName"/>.</summary>
    /// <param name="databaseName">A name from <see cref="NewName"/>.</param>
    /// <exception cref="ArgumentException">The name is not one this run created.</exception>
    public static string ConnectionStringFor(string databaseName)
    {
        RequireOwnName(databaseName);
        return WithCatalog(databaseName);
    }

    /// <summary>
    /// Creates an empty database named by <see cref="NewName"/>. For a test that builds its own schema; a fixture that migrates a
    /// database needs only its name and <see cref="ConnectionStringFor"/>.
    /// </summary>
    /// <param name="databaseName">A name from <see cref="NewName"/>.</param>
    /// <param name="collation">The database's collation, such as <c>Latin1_General_CI_AS</c>. Null uses the server's default.</param>
    /// <param name="ct">Cancels the creation.</param>
    /// <exception cref="ArgumentException">The name is not one this run created, or the collation is not a plain collation name.</exception>
    public static async Task CreateEmptyAsync(string databaseName, string? collation = null, CancellationToken ct = default)
    {
        RequireOwnName(databaseName);
        if (collation is not null && !CollationPattern.IsMatch(collation))
            throw new ArgumentException($"'{collation}' is not a plain collation name.", nameof(collation));

        var clause = collation is null ? string.Empty : " COLLATE " + collation;
        await using var master = new SqlConnection(Master);
        await master.OpenAsync(ct);
        await using var command = master.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteName(databaseName)}{clause};";
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Drops a database this run created. A database that does not exist is left alone.</summary>
    /// <param name="databaseName">A name from <see cref="NewName"/>.</param>
    /// <param name="ct">Cancels the drop.</param>
    /// <exception cref="ArgumentException">The name is not one this run created.</exception>
    public static async Task DropAsync(string databaseName, CancellationToken ct = default)
    {
        RequireOwnName(databaseName);
        await DropAnyRunAsync(databaseName, ct);
    }

    /// <summary>
    /// Drops a database of any run, for the sweep of leftovers and the end-of-run cleanup. A name that is not one of the run names
    /// is refused, so a database the tests did not create is never dropped.
    /// </summary>
    /// <param name="databaseName">The database name, which must be a run name of any run.</param>
    /// <param name="ct">Cancels the drop.</param>
    internal static async Task DropAnyRunAsync(string databaseName, CancellationToken ct = default)
    {
        if (RunIdOf(databaseName) is null)
            throw new ArgumentException(
                $"'{databaseName}' is not a test database name, so it is not dropped.", nameof(databaseName));

        // A pooled connection to the database keeps it open, and a single-user drop would wait on it.
        using (var pooled = new SqlConnection(WithCatalog(databaseName)))
            SqlConnection.ClearPool(pooled);

        await using var master = new SqlConnection(Master);
        await master.OpenAsync(ct);
        await using var command = master.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE {QuoteName(databaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {QuoteName(databaseName)};
            END
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Tries to open the server, and says why it cannot. Returns null when the server answers, otherwise the reason, which a test
    /// passes to <c>Assert.Skip</c>. The probe has a short timeout and no pooling, so an absent server answers at once.
    /// </summary>
    /// <param name="ct">Cancels the probe.</param>
    public static async Task<string?> UnreachableReasonAsync(CancellationToken ct = default)
    {
        var settings = Fixed();
        try
        {
            var probe = new SqlConnectionStringBuilder(settings.BaseConnection) { ConnectTimeout = 3, Pooling = false };
            await using var connection = new SqlConnection(probe.ConnectionString);
            await connection.OpenAsync(ct);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}. Set {settings.ConnectionVariable} to run this test.";
        }
    }

    /// <summary>
    /// The marked databases of every run other than <paramref name="ownRunId"/> that are at least <paramref name="olderThan"/> old,
    /// by the server's clock.
    /// </summary>
    /// <param name="ownRunId">This run's id, which is never returned.</param>
    /// <param name="olderThan">The age a leftover must reach.</param>
    /// <param name="ct">Cancels the query.</param>
    internal static async Task<IReadOnlyList<string>> StaleNamesAsync(string ownRunId, TimeSpan olderThan, CancellationToken ct = default)
    {
        var names = await NamesAsync(olderThan, ct);
        return names.Where(name => RunIdOf(name) is { } runId && runId != ownRunId).ToList();
    }

    /// <summary>The marked databases this run created and has not yet dropped.</summary>
    /// <param name="ct">Cancels the query.</param>
    internal static async Task<IReadOnlyList<string>> OwnNamesAsync(CancellationToken ct = default)
    {
        var names = await NamesAsync(olderThan: null, ct);
        return names.Where(IsOwnName).ToList();
    }

    private static async Task<List<string>> NamesAsync(TimeSpan? olderThan, CancellationToken ct)
    {
        // The marker is letters only (Configure checks it), so this pattern needs no escaping beyond the underscore.
        var pattern = "%[_]" + NameMarker[1..] + "%";
        await using var connection = new SqlConnection(Master);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = olderThan is null
            ? "SELECT name FROM sys.databases WHERE name LIKE @pattern;"
            : "SELECT name FROM sys.databases WHERE name LIKE @pattern AND DATEDIFF(second, create_date, GETDATE()) >= @seconds;";
        command.Parameters.AddWithValue("@pattern", pattern);
        if (olderThan is { } age)
            command.Parameters.AddWithValue("@seconds", (int)age.TotalSeconds);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));
        return names;
    }

    private static void RequireOwnName(string databaseName)
    {
        if (databaseName is null || !IsOwnName(databaseName))
            throw new ArgumentException(
                $"'{databaseName}' is not a database this test run created. Name it with TestDatabase.NewName(\"purpose\"): "
                + "a fixed name such as this one is shared with every other test run on the SQL Server, so one run "
                + $"drops a database another run is using. Each run's names carry its marker '{NameMarker}{RunId}_'.",
                nameof(databaseName));
    }

    private static string QuoteName(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string WithCatalog(string databaseName) =>
        new SqlConnectionStringBuilder(Server) { InitialCatalog = databaseName }.ConnectionString;

    /// <summary>The settings, fixed now: from <see cref="Configure"/>, or the defaults when nothing was configured.</summary>
    private static Settings Fixed()
    {
        lock (Gate)
        {
            _fixed = true;
            return _settings ??= new Settings(new TestDatabaseOptions());
        }
    }

    private sealed class Settings
    {
        public Settings(TestDatabaseOptions options)
        {
            ConnectionVariable = options.ConnectionVariable;
            DefaultConnection = options.DefaultConnection;
            Marker = options.NameMarker;
            StaleAfter = options.StaleAfter;
            RunName = new Regex(
                "^" + PurposeBody + Regex.Escape(Marker) + "([0-9a-f]{12})_[0-9]+\\z",
                RegexOptions.CultureInvariant);
        }

        public string ConnectionVariable { get; }

        public string DefaultConnection { get; }

        public string Marker { get; }

        public TimeSpan StaleAfter { get; }

        /// <summary>Matches a run name of any run, with the run id as group 1.</summary>
        public Regex RunName { get; }

        /// <summary>The environment's base connection, or the default when the variable is blank. Read on each call.</summary>
        public string BaseConnection
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
                return string.IsNullOrWhiteSpace(configured) ? DefaultConnection : configured;
            }
        }
    }
}
