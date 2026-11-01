// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Microsoft.Data.SqlClient;

namespace Ballast.SqlServer;

/// <summary>
/// Refuses a developer database outside Development. A deployment whose connection string is missing or misspelled falls back to
/// nothing useful: an app that starts with a string for this computer, or with Windows authentication, fails at its first query in
/// a container, and reads like a database outage. The check runs at startup and names the setting.
/// <para>
/// The check reads the parsed string: its data source (the server) and whether it uses Windows authentication. It does not look
/// for words anywhere in the string, so a database named <c>localhost</c> on a real server is not a developer database.
/// </para>
/// </summary>
public static class ConnectionStringCheck
{
    /// <summary>
    /// Classifies a connection string without connecting to it. The string is never written to a message, because it can hold a
    /// password.
    /// </summary>
    /// <param name="connectionString">The connection string to read. Required.</param>
    /// <returns>
    /// <see cref="ConnectionStringProblem.None"/> when the string names another computer and does not use Windows authentication;
    /// otherwise the first problem found, in this order: unreadable, LocalDB, this computer, Windows authentication.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is null.</exception>
    public static ConnectionStringProblem Classify(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // SqlClient reports a bad keyword or value as ArgumentException, and a bad number as FormatException.
            return ConnectionStringProblem.Unreadable;
        }

        var host = HostOf(builder.DataSource.Trim());
        if (host is null)
        {
            return ConnectionStringProblem.Unreadable;
        }

        if (host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionStringProblem.LocalDb;
        }

        if (IsThisComputer(host))
        {
            return ConnectionStringProblem.LocalHost;
        }

        if (builder.IntegratedSecurity)
        {
            return ConnectionStringProblem.IntegratedSecurity;
        }

        return ConnectionStringProblem.None;
    }

    /// <summary>
    /// Throws when the string is a developer database and the app is not in Development. Development is never refused.
    /// </summary>
    /// <param name="connectionString">The connection string to read. Required.</param>
    /// <param name="isDevelopment">True in the Development environment. The caller reads it; this library names no hosting environment.</param>
    /// <param name="settingName">The configuration key the string came from, named in the message (for example <c>ConnectionStrings:DefaultConnection</c>). Required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> or <paramref name="settingName"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="settingName"/> is empty or only white space.</exception>
    /// <exception cref="InvalidOperationException">
    /// The string is refused. The message names <paramref name="settingName"/> and the reason, and never the string itself.
    /// </exception>
    public static void EnsureNotDeveloperDatabase(string connectionString, bool isDevelopment, string settingName)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);

        if (isDevelopment)
        {
            return;
        }

        var problem = Classify(connectionString);
        if (problem is ConnectionStringProblem.None)
        {
            return;
        }

        throw new InvalidOperationException(MessageFor(problem, settingName));
    }

    /// <summary>
    /// The host of a data source: the part before the port (a comma) or the instance (a backslash), without a protocol prefix.
    /// Null when the source names no host.
    /// </summary>
    private static string? HostOf(string dataSource)
    {
        var rest = dataSource;

        if (rest.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[4..];
        }
        else if (rest.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase))
        {
            // Shared memory reaches only this computer, so its server is named as this computer.
            rest = ".";
        }
        else if (rest.StartsWith("np:", StringComparison.OrdinalIgnoreCase))
        {
            // A named pipe is written np:\\host\pipe\...; the host is the first part after the two slashes.
            rest = rest[3..];
            if (rest.StartsWith(@"\\", StringComparison.Ordinal))
            {
                rest = rest[2..];
            }
        }

        if (rest.StartsWith('['))
        {
            // A bracketed IPv6 literal, such as [::1],1433
            var close = rest.IndexOf(']', StringComparison.Ordinal);
            return close < 0 ? null : NonEmpty(rest[1..close]);
        }

        var end = rest.IndexOfAny([',', '\\']);
        return NonEmpty((end < 0 ? rest : rest[..end]).Trim());
    }

    private static string? NonEmpty(string text) => text.Length == 0 ? null : text;

    /// <summary>True for this computer's names: localhost, a name under localhost, '.', '(local)' and a loopback address.</summary>
    private static bool IsThisComputer(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals(".", StringComparison.Ordinal)
        || host.Equals("(local)", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static string MessageFor(ConnectionStringProblem problem, string settingName) => problem switch
    {
        ConnectionStringProblem.LocalHost =>
            $"{settingName} names a server on this computer (localhost or a loopback address). Outside Development it must name the database the app uses.",
        ConnectionStringProblem.LocalDb =>
            $"{settingName} names a LocalDB instance. Outside Development it must name the database the app uses.",
        ConnectionStringProblem.IntegratedSecurity =>
            $"{settingName} uses Windows authentication (Trusted_Connection or Integrated Security). Outside Development it must use the database login from configuration.",
        _ =>
            $"{settingName} is not a readable SQL Server connection string. It names no server, or it does not parse.",
    };
}
