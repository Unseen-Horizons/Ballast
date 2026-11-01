// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer.Tests;

/// <summary>The classifiers on exceptions built with test doubles. Each one walks the whole chain and every error in it.</summary>
public class SqlErrorsTests
{
    // ── the numbers ─────────────────────────────────────────────────

    [Fact]
    public void The_constants_are_the_numbers_the_usage_guide_names()
    {
        Assert.Equal(2601, SqlErrors.DuplicateKey);
        Assert.Equal(2627, SqlErrors.UniqueConstraint);
        Assert.Equal(1205, SqlErrors.DeadlockVictim);
        Assert.Equal(1222, SqlErrors.LockTimeout);
        Assert.Equal(258, SqlErrors.WaitTimeout);
        Assert.Equal(-2, SqlErrors.CommandTimeout);
    }

    // ── unique violations ───────────────────────────────────────────

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void IsUniqueViolation_IsTrue_ForADuplicateKeyAndAUniqueConstraint(int number)
    {
        Assert.True(SqlErrors.IsUniqueViolation(SqlExceptions.Create(number)));
        Assert.True(SqlErrors.IsUniqueViolation(new DbUpdateException("save failed", SqlExceptions.Create(number))));
    }

    [Theory]
    [InlineData(547)]
    [InlineData(2628)]
    [InlineData(8152)]
    [InlineData(515)]
    [InlineData(4060)]
    [InlineData(18456)]
    [InlineData(50000)]
    [InlineData(1205)]
    [InlineData(-2)]
    public void IsUniqueViolation_IsFalse_ForEveryOtherSqlError(int number)
    {
        Assert.False(SqlErrors.IsUniqueViolation(SqlExceptions.Create(number)));
        Assert.False(SqlErrors.IsUniqueViolation(new DbUpdateException("save failed", SqlExceptions.Create(number))));
    }

    [Fact]
    public void IsUniqueViolation_FindsTheSqlException_however_deep_it_sits()
    {
        var duplicate = SqlExceptions.Create(2601);

        Assert.True(SqlErrors.IsUniqueViolation(duplicate), "directly");
        Assert.True(SqlErrors.IsUniqueViolation(new DbUpdateException("save failed", duplicate)), "inside a DbUpdateException");
        Assert.True(
            SqlErrors.IsUniqueViolation(new InvalidOperationException("outer", new DbUpdateException("save failed", new InvalidOperationException("middle", duplicate)))),
            "however deep in the InnerException chain");
    }

    [Fact]
    public void IsUniqueViolation_LooksAtEveryErrorOfTheException_NotJustTheFirst()
    {
        Assert.True(SqlErrors.IsUniqueViolation(SqlExceptions.Create(547, 2601)));
    }

    [Fact]
    public void IsUniqueViolation_LooksInsideAnAggregateException()
    {
        var aggregate = new AggregateException(new InvalidOperationException("other"), new DbUpdateException("save failed", SqlExceptions.Create(2627)));

        Assert.True(SqlErrors.IsUniqueViolation(aggregate));
    }

    [Fact]
    public void IsUniqueViolation_IsFalse_ForExceptionsWithoutASqlException()
    {
        Assert.False(SqlErrors.IsUniqueViolation(new InvalidOperationException("plain")));
        Assert.False(SqlErrors.IsUniqueViolation(new DbUpdateException("save failed", new InvalidOperationException("other"))));
        Assert.False(SqlErrors.IsUniqueViolation(new AggregateException()));
    }

    // ── deadlocks ───────────────────────────────────────────────────

    [Fact]
    public void IsDeadlock_IsTrue_ForADeadlockVictim_directly_wrapped_and_deeper()
    {
        var victim = SqlExceptions.Create(1205);

        Assert.True(SqlErrors.IsDeadlock(victim));
        Assert.True(SqlErrors.IsDeadlock(new DbUpdateException("save failed", victim)));
        Assert.True(SqlErrors.IsDeadlock(new InvalidOperationException("outer", new DbUpdateException("save", new InvalidOperationException("middle", victim)))));
    }

    [Fact]
    public void IsDeadlock_LooksAtEveryErrorOfTheException_NotJustTheFirst()
    {
        Assert.True(SqlErrors.IsDeadlock(SqlExceptions.Create(547, 1205)));
    }

    [Fact]
    public void IsDeadlock_StaysTheDeadlockOnly_BecauseTheRetryAtOnceLoopUsesIt()
    {
        Assert.False(SqlErrors.IsDeadlock(SqlExceptions.Create(1222)), "a lock timeout is not retried at once");
        Assert.False(SqlErrors.IsDeadlock(SqlExceptions.Create(-2)), "a command timeout is not retried at once");
        Assert.False(SqlErrors.IsDeadlock(new InvalidOperationException("plain")));
    }

    // ── busy ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1205)]
    [InlineData(1222)]
    [InlineData(-2)]
    public void IsBusy_IsTrue_ForADeadlockVictim_ALockTimeout_AndACommandTimeout(int number)
    {
        Assert.True(SqlErrors.IsBusy(SqlExceptions.Create(number)));
    }

    [Fact]
    public void IsBusy_FindsTheSqlExceptionDirectly_inADbUpdateException_AndDeeperDown()
    {
        var busy = SqlExceptions.Create(1205);

        Assert.True(SqlErrors.IsBusy(busy), "directly");
        Assert.True(SqlErrors.IsBusy(new DbUpdateException("save failed", busy)), "inside a DbUpdateException, as SaveChanges throws it");
        Assert.True(
            SqlErrors.IsBusy(new InvalidOperationException("outer", new DbUpdateException("save failed", new InvalidOperationException("middle", busy)))),
            "however deep in the InnerException chain");
    }

    [Fact]
    public void IsBusy_LooksAtEveryErrorOfTheException_NotJustTheFirst()
    {
        Assert.True(SqlErrors.IsBusy(SqlExceptions.Create(547, 1205)));
    }

    [Fact]
    public void IsBusy_IsTrue_ForATimeoutException_anywhereInTheChain()
    {
        // The rule of the database-busy handler, which answers 503 for it.
        Assert.True(SqlErrors.IsBusy(new TimeoutException("a database timeout")));
        Assert.True(SqlErrors.IsBusy(new DbUpdateException("save failed", new TimeoutException("inner"))));
    }

    [Theory]
    [InlineData(547)]
    [InlineData(2601)]
    [InlineData(2627)]
    [InlineData(2628)]
    [InlineData(8152)]
    [InlineData(515)]
    [InlineData(4060)]
    [InlineData(18456)]
    [InlineData(50000)]
    [InlineData(258)]
    public void IsBusy_IsFalse_ForEveryOtherSqlError_including_the_wait_timeout(int number)
    {
        Assert.False(SqlErrors.IsBusy(SqlExceptions.Create(number)));
        Assert.False(SqlErrors.IsBusy(new DbUpdateException("save failed", SqlExceptions.Create(number))));
    }

    [Fact]
    public void IsBusy_IsFalse_ForExceptionsWithoutASqlExceptionOrATimeout()
    {
        Assert.False(SqlErrors.IsBusy(new InvalidOperationException("plain")));
        Assert.False(SqlErrors.IsBusy(new DbUpdateException("save failed", new InvalidOperationException("other"))));
        Assert.False(SqlErrors.IsBusy(new OperationCanceledException("cancelled")));
    }

    // ── transient ───────────────────────────────────────────────────

    [Theory]
    [InlineData(258)]
    [InlineData(1205)]
    [InlineData(1222)]
    [InlineData(-2)]
    public void IsTransient_IsTrue_ForEveryNumberOfTheReconcileRetryList(int number)
    {
        Assert.True(SqlErrors.IsTransient(SqlExceptions.Create(number)));
        Assert.True(SqlErrors.IsTransient(new DbUpdateException("save failed", SqlExceptions.Create(number))));
    }

    [Fact]
    public void IsTransient_IsTrue_ForATimeoutException_anywhereInTheChain()
    {
        Assert.True(SqlErrors.IsTransient(new TimeoutException("a database timeout")));
    }

    [Fact]
    public void IsTransient_IsBusy_plusTheWaitTimeout()
    {
        // The wait timeout is retried, but it does not make the database "busy" for a 503.
        Assert.True(SqlErrors.IsTransient(SqlExceptions.Create(258)));
        Assert.False(SqlErrors.IsBusy(SqlExceptions.Create(258)));
        Assert.False(SqlErrors.IsTransient(SqlExceptions.Create(547)));
        Assert.False(SqlErrors.IsTransient(new InvalidOperationException("plain")));
    }

    // ── error numbers ───────────────────────────────────────────────

    [Fact]
    public void ErrorNumbers_IsEmpty_ForAnExceptionWithoutASqlError()
    {
        Assert.Empty(SqlErrors.ErrorNumbers(new InvalidOperationException("plain")));
        Assert.Empty(SqlErrors.ErrorNumbers(new AggregateException()));
    }

    [Fact]
    public void ErrorNumbers_ListsEveryErrorOfEverySqlExceptionInTheChain_inOrder_eachNumberOnce()
    {
        var chain = new AggregateException(
            new DbUpdateException("first save", SqlExceptions.Create(547, 1205)),
            SqlExceptions.Create(1205, 2601));
        int[] expected = [547, 1205, 2601];

        Assert.Equal(expected, SqlErrors.ErrorNumbers(chain));
    }

    [Fact]
    public void ErrorNumbers_FindsASqlErrorInsideAnInnerChain()
    {
        var error = new InvalidOperationException("outer", new DbUpdateException("save", new InvalidOperationException("middle", SqlExceptions.Create(2627))));
        int[] expected = [2627];

        Assert.Equal(expected, SqlErrors.ErrorNumbers(error));
    }

    // ── null ────────────────────────────────────────────────────────

    [Fact]
    public void Every_check_refuses_null()
    {
        Assert.Throws<ArgumentNullException>(() => SqlErrors.ErrorNumbers(null!));
        Assert.Throws<ArgumentNullException>(() => SqlErrors.IsUniqueViolation(null!));
        Assert.Throws<ArgumentNullException>(() => SqlErrors.IsDeadlock(null!));
        Assert.Throws<ArgumentNullException>(() => SqlErrors.IsBusy(null!));
        Assert.Throws<ArgumentNullException>(() => SqlErrors.IsTransient(null!));
    }
}
