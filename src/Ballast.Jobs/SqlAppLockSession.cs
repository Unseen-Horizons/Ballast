// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Data;
using System.Globalization;

using Microsoft.Data.SqlClient;

namespace Ballast.Jobs;

/// <summary>
/// An <see cref="IAppLockSession"/> on SQL Server: <c>sp_getapplock</c> with the session as owner, over a connection of its own.
/// <para>
/// The connection is the app's own connection string with pooling and connection retry off and an application name that says
/// what it is for. Without pooling, closing the connection ends the session for real, which frees the lock; and with no
/// connection retry a dropped connection is a lost lock, not a silent reconnect to a session that no longer holds anything.
/// Nothing is stored: an application lock is not a row.
/// </para>
/// </summary>
public sealed class SqlAppLockSession : IAppLockSession
{
    private const string Exclusive = "Exclusive";
    private const string SessionOwner = "Session";

    private readonly SqlConnection _connection;

    /// <summary>Creates the session. The connection is opened by <see cref="OpenAsync"/>.</summary>
    /// <param name="connectionString">The app's connection string to the database that holds the lock. Pooling and connection retry are set off here.</param>
    /// <param name="applicationName">The application name the connection shows in the server's session list.</param>
    public SqlAppLockSession(string connectionString, string applicationName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ConnectRetryCount = 0,
            ApplicationName = applicationName,
        };
        _connection = new SqlConnection(builder.ConnectionString);
    }

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken ct) => _connection.OpenAsync(ct);

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(string resource, TimeSpan timeout, CancellationToken ct)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;

        // The call waits on the server for up to the lock timeout, so the command has to outlast it.
        command.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds) + 15;
        command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@LockMode", SqlDbType.VarChar, 32) { Value = Exclusive });
        command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = SessionOwner });
        command.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue) });
        var result = new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
        command.Parameters.Add(result);

        await command.ExecuteNonQueryAsync(ct);

        // 0: granted at once. 1: granted after waiting. -1: still held by another session when the wait ended.
        // -2 (cancelled), -3 (deadlock victim) and -999 (a call error) are not "busy", so they are faults.
        var code = result.Value is int value ? value : int.MinValue;
        if (code >= 0)
        {
            return true;
        }

        if (code == -1)
        {
            return false;
        }

        throw new InvalidOperationException(
            $"sp_getapplock for '{resource}' returned {code.ToString(CultureInfo.InvariantCulture)}.");
    }

    /// <inheritdoc />
    public async Task<bool> HoldsAsync(string resource, CancellationToken ct)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT APPLOCK_MODE('public', @Resource, 'Session');";
        command.CommandTimeout = 20;
        command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = resource });

        var mode = await command.ExecuteScalarAsync(ct);
        return mode is string text && string.Equals(text, Exclusive, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string resource, CancellationToken ct)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "sp_releaseapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = 10;
        command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = SessionOwner });

        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Closes the connection. With pooling off this ends the session, which frees any lock it still holds.</summary>
    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
