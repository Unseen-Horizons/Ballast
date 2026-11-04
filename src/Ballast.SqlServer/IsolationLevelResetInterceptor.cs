// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ballast.SqlServer;

/// <summary>
/// Puts a connection back to READ COMMITTED before it is closed when it ran a transaction at another isolation level.
/// <para>
/// SQL Server keeps the isolation level of a session when the connection goes back to the pool. After a SERIALIZABLE transaction,
/// the next request that gets that pooled connection runs its plain reads as SERIALIZABLE too: it takes range locks and can
/// deadlock with the next serializable write. The transaction itself keeps the level it asked for. Only what runs on the connection
/// after it is reset.
/// </para>
/// <para>
/// Add it to the context's options: <c>options.UseSqlServer(cs).AddInterceptors(IsolationLevelResetInterceptor.Instance)</c>.
/// </para>
/// </summary>
public sealed class IsolationLevelResetInterceptor : IDbTransactionInterceptor, IDbConnectionInterceptor
{
    /// <summary>The one interceptor. It keeps no state of its own: the open connections are tracked per connection.</summary>
    public static readonly IsolationLevelResetInterceptor Instance = new();

    private static readonly object Due = new();

    // The open connections that ran such a transaction. Weak keys: a connection that is never closed is not kept alive by this.
    private readonly ConditionalWeakTable<DbConnection, object> _resetDue = new();

    private IsolationLevelResetInterceptor() { }

    /// <inheritdoc />
    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Remember(connection, result);
        return result;
    }

    /// <inheritdoc />
    public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        Remember(connection, result);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (TakeResetDue(connection))
        {
            using var command = CreateResetCommand(connection);
            command.ExecuteNonQuery();
        }
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (TakeResetDue(connection))
        {
            await using var command = CreateResetCommand(connection);
            await command.ExecuteNonQueryAsync();
        }
        return result;
    }

    /// <inheritdoc />
    /// <remarks>A context that is disposed while its connection is still open closes the connection by disposing it.</remarks>
    public InterceptionResult ConnectionDisposing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        ConnectionClosing(connection, eventData, result);

    /// <inheritdoc />
    public ValueTask<InterceptionResult> ConnectionDisposingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        ConnectionClosingAsync(connection, eventData, result);

    private void Remember(DbConnection connection, DbTransaction transaction)
    {
        if (transaction.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Unspecified))
        {
            _resetDue.AddOrUpdate(connection, Due);
        }
    }

    // True once per transaction of another level, and only while the connection is open: a broken connection is not pooled again.
    private bool TakeResetDue(DbConnection connection) =>
        _resetDue.Remove(connection) && connection.State == ConnectionState.Open;

    private static DbCommand CreateResetCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED";
        return command;
    }
}
