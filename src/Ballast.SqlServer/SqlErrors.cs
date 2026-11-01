// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer;

/// <summary>
/// Classifies the SQL Server errors inside an exception. Every check looks through the whole exception: each inner exception,
/// each inner exception of an <see cref="AggregateException"/>, and each error that a <see cref="SqlException"/> carries. EF Core
/// wraps a failed save in a <see cref="DbUpdateException"/> and an execution strategy may wrap it again, so the SQL error is
/// rarely the outer exception. A check that looked only at the direct inner exception would miss it.
/// <para>
/// The checks answer with numbers and classifications only. Texts for a user, and any decision about what to retry, stay in the
/// app. The per-number sources are in the usage guide (docs/SqlServer.md, "The error numbers").
/// </para>
/// </summary>
public static class SqlErrors
{
    /// <summary>SQL Server error 2601: a row was inserted or updated with a duplicate key in a unique index.</summary>
    public const int DuplicateKey = 2601;

    /// <summary>SQL Server error 2627: a unique or primary key constraint was violated.</summary>
    public const int UniqueConstraint = 2627;

    /// <summary>SQL Server error 1205: the request was chosen as the victim of a deadlock and rolled back.</summary>
    public const int DeadlockVictim = 1205;

    /// <summary>SQL Server error 1222: a lock was not granted within the lock timeout.</summary>
    public const int LockTimeout = 1222;

    /// <summary>SQL Server error 258: a wait operation timed out. A client may try it again.</summary>
    public const int WaitTimeout = 258;

    /// <summary>
    /// The number SqlClient reports for a command or connection timeout (including a wait for a free pooled connection). It is
    /// not a SQL Server error number.
    /// </summary>
    public const int CommandTimeout = -2;

    /// <summary>
    /// The SQL error numbers found in the exception: every error of every <see cref="SqlException"/> in its chain, in the order
    /// they first appear, each number once. An exception with no SQL error gives an empty list.
    /// </summary>
    /// <param name="exception">The exception to read. Required.</param>
    /// <returns>The distinct error numbers. Never null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static IReadOnlyList<int> ErrorNumbers(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        List<int> numbers = [];
        foreach (var sql in Walk(exception).OfType<SqlException>())
        {
            foreach (var error in sql.Errors.Cast<SqlError>())
            {
                AddDistinct(numbers, error.Number);
            }
            AddDistinct(numbers, sql.Number);
        }
        return numbers;
    }

    /// <summary>
    /// True when the save or statement failed on a unique index (2601) or a unique constraint (2627): the row another writer just
    /// inserted, not a fault. Any other database failure is not this.
    /// </summary>
    /// <param name="exception">The exception to read. Required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static bool IsUniqueViolation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return AnySqlError(exception, static number => number is DuplicateKey or UniqueConstraint);
    }

    /// <summary>
    /// True when the request was the deadlock victim (1205). A retry loop that retries at once uses this check, so it stays
    /// deadlock-only: a lock timeout or a command timeout is not retried at once.
    /// </summary>
    /// <param name="exception">The exception to read. Required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static bool IsDeadlock(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return AnySqlError(exception, static number => number == DeadlockVictim);
    }

    /// <summary>
    /// True when the database was too busy to answer, and the same request is likely to succeed a moment later: a deadlock victim
    /// (1205), a lock timeout (1222), a command or connection timeout (-2), or a <see cref="TimeoutException"/> anywhere in the
    /// chain. An app answers such a request with 503 and asks the client to try again. This check does not include wait timeout 258;
    /// <see cref="IsTransient"/> does.
    /// </summary>
    /// <param name="exception">The exception to read. Required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static bool IsBusy(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return AnySqlError(exception, static number => number is DeadlockVictim or LockTimeout or CommandTimeout)
            || Walk(exception).Any(static e => e is TimeoutException);
    }

    /// <summary>
    /// True when the same request is worth one more attempt rather than a parked job or a failed call: every error that
    /// <see cref="IsBusy"/> names, plus a wait timeout (258). A <see cref="TimeoutException"/> anywhere in the chain counts too.
    /// </summary>
    /// <param name="exception">The exception to read. Required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return AnySqlError(exception, static number => number is WaitTimeout or DeadlockVictim or LockTimeout or CommandTimeout)
            || Walk(exception).Any(static e => e is TimeoutException);
    }

    /// <summary>
    /// Every exception under <paramref name="root"/>, depth first, in the order they appear: the root, then its inner exception, or
    /// for an <see cref="AggregateException"/> each of its inner exceptions in turn. Each exception is visited once, so a cyclic
    /// chain ends.
    /// </summary>
    private static IEnumerable<Exception> Walk(Exception root)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;

            if (current is AggregateException aggregate)
            {
                PushInReverse(pending, aggregate.InnerExceptions);
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }

    /// <summary>Pushes the children last to first, so the first child is the next one popped.</summary>
    private static void PushInReverse(Stack<Exception> pending, ReadOnlyCollection<Exception> children)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            pending.Push(children[i]);
        }
    }

    /// <summary>True when the number, or any error number, of a SQL exception in the chain satisfies the predicate.</summary>
    private static bool AnySqlError(Exception exception, Func<int, bool> predicate)
    {
        foreach (var sql in Walk(exception).OfType<SqlException>())
        {
            if (predicate(sql.Number))
            {
                return true;
            }
            foreach (var error in sql.Errors.Cast<SqlError>())
            {
                if (predicate(error.Number))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void AddDistinct(List<int> numbers, int number)
    {
        if (!numbers.Contains(number))
        {
            numbers.Add(number);
        }
    }
}
