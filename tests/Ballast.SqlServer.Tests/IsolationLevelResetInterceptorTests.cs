// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Data;
using System.Data.Common;
using System.Transactions;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using IsolationLevel = System.Data.IsolationLevel;

namespace Ballast.SqlServer.Tests;

/// <summary>
/// SQL Server keeps the isolation level of a session when the connection goes back to the pool. After a SERIALIZABLE transaction a
/// context must leave its connection at READ COMMITTED, or the next request that gets the connection from the pool runs its plain
/// reads as SERIALIZABLE (<see cref="IsolationLevelResetInterceptor"/>). Every connection string here has a pool of its own (a unique
/// application name) and Enlist=false, and the tests run outside any ambient transaction, so the next connection opened is the one
/// that was just closed.
/// </summary>
public class IsolationLevelResetInterceptorTests
{
    private const short ReadCommitted = 2;
    private const short Serializable = 4;

    private static string OwnPool(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { Enlist = false, ApplicationName = "isolation-reset-" + Guid.NewGuid().ToString("N") }.ConnectionString;

    private static TransactionScope OutsideAmbientTransaction() => new(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);

    /// <summary>The session id and isolation level of the connection, read inside the transaction when there is one.</summary>
    private static async Task<(short SessionId, short Level)> ReadSessionAsync(DbConnection connection, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT session_id, transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "a session can always see its own row");
        return (reader.GetInt16(0), reader.GetInt16(1));
    }

    /// <summary>The session id and isolation level of the next connection the pool hands out.</summary>
    private static async Task<(short SessionId, short Level)> NextPooledSessionAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return await ReadSessionAsync(connection);
    }

    [Fact]
    public async Task Without_the_interceptor_a_pooled_connection_keeps_the_level_of_its_last_transaction()
    {
        // What makes the reset needed.
        await using var database = await SqlTestDatabase.CreateAsync();
        using var outside = OutsideAmbientTransaction();
        var pool = OwnPool(database.ConnectionString);
        (short SessionId, short Level) inside;

        await using (var connection = new SqlConnection(pool))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            inside = await ReadSessionAsync(connection, transaction);
            await transaction.CommitAsync();
        }

        var next = await NextPooledSessionAsync(pool);
        Assert.Equal(inside.SessionId, next.SessionId);
        Assert.Equal(Serializable, next.Level);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]   // disposed without a commit, as when a booking is refused inside its transaction
    [InlineData(false, true)]
    public async Task After_a_serializable_transaction_of_the_context_the_pooled_connection_is_back_at_read_committed(bool async, bool commit)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        using var outside = OutsideAmbientTransaction();
        var pool = OwnPool(database.ConnectionString);
        (short SessionId, short Level) inside;

        await using (var db = SampleContext.Create(pool, withResetInterceptor: true))
        {
            if (async)
            {
                var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                inside = await ReadSessionAsync(db.Database.GetDbConnection(), transaction.GetDbTransaction());
                if (commit)
                {
                    await transaction.CommitAsync();
                }
                await transaction.DisposeAsync();
            }
            else
            {
                var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
                inside = await ReadSessionAsync(db.Database.GetDbConnection(), transaction.GetDbTransaction());
                if (commit)
                {
                    transaction.Commit();
                }
                transaction.Dispose();
            }
        }

        Assert.Equal(Serializable, inside.Level);
        var next = await NextPooledSessionAsync(pool);
        Assert.Equal(inside.SessionId, next.SessionId);
        Assert.Equal(ReadCommitted, next.Level);
    }

    [Fact]
    public async Task A_read_committed_transaction_of_the_context_leaves_the_connection_at_read_committed()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        using var outside = OutsideAmbientTransaction();
        var pool = OwnPool(database.ConnectionString);

        await using (var db = SampleContext.Create(pool, withResetInterceptor: true))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await transaction.CommitAsync();
        }

        Assert.Equal(ReadCommitted, (await NextPooledSessionAsync(pool)).Level);
    }
}
