# Ballast.SqlServer: usage guide

Ballast.SqlServer holds the SQL Server and Entity Framework Core rules that an app would otherwise copy into each of its contexts:
classify a SQL error wherever EF Core wraps it, store enums as text, restrict deletes, run a change in a transaction that retries
cleanly, claim a row so that one request wins, read a sequence, split a script on its `GO` lines, reset a pooled connection's
isolation level, check that a database compares text without case, and refuse a developer database in production.

The package has the namespace `Ballast.SqlServer`, targets `net10.0`, and depends on `Microsoft.EntityFrameworkCore.SqlServer` 10.0.0
and `Microsoft.Data.SqlClient` 6.1.7. It depends on no other Ballast library and no app.

The package README has a short example that runs as written. This guide covers every public member, what each one accepts, and
every way each one fails.

**Trimming and AOT.** The library is not trimmable and not AOT-compatible, because Entity Framework Core is not. A trimmed or
AOT-compiled app cannot use it.

## How the pieces fit

1. The context's `OnModelCreating` calls `UseEnumsAsStrings` and `RestrictDeletes`, at the point where the app's own loops ran.
2. The context's options add `IsolationLevelResetInterceptor.Instance`, when the app uses SERIALIZABLE transactions.
3. A handler runs its change with `ExecuteInTransactionAsync`, or a claim with `ClaimAsync`. A catch block asks `SqlErrors` what
   the failure was. A sequence is read with `NextSequenceValueAsync`.
4. At startup, `ConnectionStringCheck` and `CollationCheck` refuse what must not run in production.

The library returns numbers and classifications only. A message for a user, a decision about how many times to retry, and the
HTTP status all stay in the app.

## SqlErrors

```csharp
public static class SqlErrors
```

The SQL Server error classes. Every check walks the whole exception: each inner exception, each inner exception of an
`AggregateException`, and each error that a `SqlException` carries. A `DbUpdateException` wraps the SQL error, and an
execution strategy may wrap it again, so a check that looked at the direct inner exception would miss it.

### Constants

| Constant | Value | Meaning |
|---|---|---|
| `DuplicateKey` | 2601 | A duplicate key in a unique index. |
| `UniqueConstraint` | 2627 | A unique or primary key constraint was violated. |
| `DeadlockVictim` | 1205 | The request was chosen as a deadlock victim and rolled back. |
| `LockTimeout` | 1222 | A lock was not granted within the lock timeout. |
| `WaitTimeout` | 258 | A wait operation timed out. |
| `CommandTimeout` | -2 | SqlClient's command or connection timeout. It is not a SQL Server error number. |

### Members

| Member | Returns true or gives | Meaning |
|---|---|---|
| `ErrorNumbers(Exception)` | `IReadOnlyList<int>` | The SQL error numbers in the chain, each once, in the order they first appear. Empty when there is none. |
| `IsUniqueViolation(Exception)` | `bool` | A `DuplicateKey` or `UniqueConstraint` error. A save that hit a unique index is a race that another writer won, not a fault. |
| `IsDeadlock(Exception)` | `bool` | A `DeadlockVictim` error. A retry loop that retries at once uses this, so it stays deadlock-only. |
| `IsBusy(Exception)` | `bool` | A deadlock victim, a lock timeout, a command timeout, or a `TimeoutException` anywhere in the chain. The database could not answer, and the same request is likely to succeed a moment later. An app answers 503 for it. Does not include the wait timeout (258). |
| `IsTransient(Exception)` | `bool` | Everything `IsBusy` names, plus the wait timeout (258). The union of the transient lists the apps had. |

**Where each number came from.** The apps carried separate lists, each for its own check. The library gives their union, and each
number is named by the check that used it.

| Number | Meaning | The checks that used it |
|---|---|---|
| 2601, 2627 | Unique index or constraint | The unique-violation check. Two copies existed: one read only the direct inner exception, the other walked the chain. |
| 1205 | Deadlock victim | The deadlock check that retries at once; the busy check; the purge and backfill retries that catch it; the busy answer (503); the reconcile retry. |
| 1222 | Lock request timeout | The busy check; the busy answer (503); the reconcile retry. |
| -2 | SqlClient command or connection timeout | The busy check; the busy answer (503); the reconcile retry. |
| 258 | Wait operation timed out | The reconcile retry only. |
| `TimeoutException` (not a SQL number) | A client-side timeout | The busy answer (503) only. |

Not included: the numbers EF Core's own `EnableRetryOnFailure` retries. The app keeps its `EnableRetryOnFailure` call, and EF Core
decides what it retries.

### Ways it fails

Each check throws `ArgumentNullException` for a null exception. No check throws for any other input. A check answers `false` or
an empty list when the exception has no SQL error and no timeout.

## ModelBuilderExtensions

```csharp
public static class ModelBuilderExtensions
```

Two model conventions. Each is a loop over the whole model, so the app calls each one from `OnModelCreating` at the same point the
app's own loop ran: after the base configuration and any identity or AI tables, before the context's own entity configuration.

### UseEnumsAsStrings

```csharp
public static ModelBuilder UseEnumsAsStrings(this ModelBuilder modelBuilder, int? maxLength = null)
```

Stores every enum property of the model as its name in text, nullable enums included, through an `EnumToStringConverter`.

| Option | Default | Meaning |
|---|---|---|
| `maxLength` | `null` | The column length in characters. `null` leaves the length to EF Core, which gives `nvarchar(max)`. A value must be positive. |

Result, checked against the two apps' models: a required enum is `NOT NULL`, a nullable enum is nullable, and the column is
`nvarchar(max)` with no `maxLength`, or `nvarchar(n)` when a length is given. Non-enum properties are not changed. The test suite
builds the apps' own loops and this convention side by side, and compares every property's provider type, column type, length,
nullability and converter, and every foreign key's delete rule.

Returns `modelBuilder`, so calls can be chained.

**Ways it fails:**

| Input | Exception |
|---|---|
| `modelBuilder` is null | `ArgumentNullException` |
| `maxLength` is zero or negative | `ArgumentOutOfRangeException` (`maxLength`) |

An enum property added to the model after the call (for example by a configuration that runs later) gets no converter. Call it after
the base configuration, as described above.

### RestrictDeletes

```csharp
public static ModelBuilder RestrictDeletes(this ModelBuilder modelBuilder)
```

Sets every foreign key that exists when it runs to `DeleteBehavior.Restrict`. SQL Server refuses several cascade paths into one
table, so a model with many relationships needs this.

A relationship configured after the call (with its own `HasOne`) is not changed here. It gets EF Core's default unless it sets
`OnDelete` itself. An app that wants a cascade, such as a child row that goes with its parent, sets it after this call.

Returns `modelBuilder`.

**Ways it fails:** a null `modelBuilder` throws `ArgumentNullException`. Nothing else fails.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.UseEnumsAsStrings(maxLength: 50);
    modelBuilder.RestrictDeletes();

    modelBuilder.Entity<Comment>(b => b.HasOne(c => c.Post).WithMany().HasForeignKey(c => c.PostId).OnDelete(DeleteBehavior.Cascade));
}
```

## TransactionExtensions

```csharp
public static class TransactionExtensions
```

Runs a change in one transaction under the context's execution strategy, and keeps the change tracker in step with the database
when the strategy retries.

### ExecuteInTransactionAsync

```csharp
public static Task ExecuteInTransactionAsync(this DbContext db, Func<CancellationToken, Task> work, IsolationLevel? isolationLevel = null, CancellationToken ct = default)
public static Task<T> ExecuteInTransactionAsync<T>(this DbContext db, Func<CancellationToken, Task<T>> work, IsolationLevel? isolationLevel = null, CancellationToken ct = default)
```

| Parameter | Default | Meaning |
|---|---|---|
| `work` | none | The change. It receives the token and must pass it on. It runs more than once if it is retried, so it must be safe to run again. |
| `isolationLevel` | `null` | The isolation level of a transaction this call opens. `null` uses the provider's default. |
| `ct` | `default` | Cancels the work, the retries and the commit. |

What it does:

- **Joining.** When a transaction is already open on the context, the work runs inside it. The call neither opens, commits nor rolls
  back, and `isolationLevel` is not applied: the open transaction keeps its own level. The owner decides the outcome.
- **Opening.** Otherwise the call runs the work in a new transaction, inside `CreateExecutionStrategy().ExecuteAsync`, and commits
  when the work returns. Without a retrying strategy, the work runs once.
- **Retries.** When the strategy replays the work after a transient fault, it first runs `ResetForRetryAsync`, so the tracked state
  matches the database. The first attempt is not reset.
- **Reset rule.** The reset detaches added entries and reloads the rest (`ResetForRetryAsync`). The other rule in use, clearing the
  whole tracker, was rejected: clearing detaches every entity, including one the caller still holds, so a later change to that object
  is silently not saved. Reloading keeps each object and refreshes its values. Its cost is one query per tracked entity with a key. A
  context that tracks thousands of rows (an import) clears its tracker itself, as import code does.

**Ways it fails:**

| Case | What happens |
|---|---|
| `db` or `work` is null | `ArgumentNullException`, thrown when the call is made. |
| The work throws, and the failure is not one the strategy retries | The transaction rolls back and the exception is thrown unchanged. |
| The failure is transient, and the retries run out | The strategy throws `RetryLimitExceededException`, with the last failure as its inner exception. `SqlErrors` finds that failure through the chain. |
| The commit fails | The exception is thrown. A transient commit fault may be retried, and the work then runs again from the top. A commit that succeeded before the fault is then seen as a second run, so write the work so that a second run is safe (a conditional update, or a claim). |
| The work is cancelled | `OperationCanceledException`, and the transaction rolls back. |
| A row the caller added before the call, and did not save, is on a retry | It is detached by the reset. Add rows inside the work. |
| The work captures a variable from outside (a flag, a result, a list) | The reset does not touch it. The work resets its own captured state at its top, on a second run, the way its own code must. |

### ResetForRetryAsync

```csharp
public static Task ResetForRetryAsync(this DbContext db, CancellationToken ct = default)
```

Detaches every added entry, then reloads each other tracked entry that has a key (unchanged, modified or deleted) from the database.
An entry whose row no longer exists is detached by the reload. This is the same reset `ExecuteInTransactionAsync` runs on a retry.
Call it yourself only when you run your own retry loop.

**Ways it fails:** a null `db` throws `ArgumentNullException`. A reload that the database refuses throws the database's exception.

## ClaimAsync

```csharp
public static Task<bool> ClaimAsync<TEntity>(this IQueryable<TEntity> claim, Action<UpdateSettersBuilder<TEntity>> changes, CancellationToken ct = default)
```

The claim pattern: one conditional `UPDATE`. The caller names the row by its key and the state it expects, and sets the new values.
Two requests that race for the same row cannot both win: the second finds the row no longer in the expected state.

```csharp
var won = await _db.Orders
    .Where(o => o.Id == order.Id && o.Status == OrderStatus.Pending)
    .ClaimAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid), ct);
```

| Parameter | Meaning |
|---|---|
| `claim` | The query with the key and the expected state in its `WHERE`. It must match at most one row. |
| `changes` | The new values, in `ExecuteUpdateAsync`'s form (EF Core 10 takes an action, not an expression). |

**Result.** `true` when one row changed (this caller won). `false` when no row matched: another request changed it first, or the row
is not in the expected state, or it does not exist. The caller decides what `false` means. A `false` answer never overwrites anything.

**Ways it fails:**

| Case | What happens |
|---|---|
| `claim` or `changes` is null | `ArgumentNullException`. |
| The update changed more than one row | `InvalidOperationException`, with the number of rows. The query is wrong: its `WHERE` did not name a key. |
| The database refuses the update | The database's exception is thrown. The claim is not decided, so the caller cannot tell whether it won. Run a claim inside a transaction and retry the whole transaction. |

The helper cannot check that the query has a state condition. A claim without one overwrites the row. Outside a transaction, a claim
that changed more than one row has already written those rows when the exception is thrown. Run a claim inside a transaction (for
example through `ExecuteInTransactionAsync`) when a rollback must undo it.

## IsolationLevelResetInterceptor

```csharp
public sealed class IsolationLevelResetInterceptor : IDbTransactionInterceptor, IDbConnectionInterceptor
public static readonly IsolationLevelResetInterceptor Instance
```

SQL Server keeps a session's isolation level when its connection goes back to the pool. After a SERIALIZABLE transaction, the next
request that gets the connection runs its plain reads as SERIALIZABLE too. The interceptor sets the connection back to READ
COMMITTED before the connection is closed. The transaction keeps the level it asked for; only what runs afterwards is reset.

```csharp
options.UseSqlServer(connectionString).AddInterceptors(IsolationLevelResetInterceptor.Instance);
```

The instance is shared by every context. It records, per connection, that a transaction of another level ran on it, and the record goes
when the connection closes or is collected. It resets a connection only while that connection is still open. A read-committed
transaction is not recorded.

**Ways it fails:**

| Case | What happens |
|---|---|
| The reset command fails when the connection closes | The exception from the reset command is thrown from the close, unchanged. The interceptor does not catch it. |
| The connection is broken when it closes | Nothing is reset. A broken connection is not pooled again. |
| The connection is never closed | Nothing is reset, and the interceptor does not keep the connection alive. |

## SqlBatches

```csharp
public static class SqlBatches
public static IReadOnlyList<string> Split(string sql)
```

Splits a script on its `GO` lines, the batch separator of the sqlcmd and SSMS tools. `GO` is not T-SQL, so each batch is sent on its own.

Rules:

- A line is a separator only when it holds nothing but `GO`: any case, with optional white space and an optional `;`. `GOTO x`, `-- GO`
  and `SELECT 'GO'` are not separators.
- The text between separators is kept as written, its line endings included.
- A batch that holds only white space is dropped, and so are consecutive separators.
- An empty script gives an empty list.

**Ways it fails:** a null `sql` throws `ArgumentNullException`. The method does not read files: the app reads its own script, and then
calls `Split`.

## SequenceExtensions

```csharp
public static class SequenceExtensions
public static Task<T> NextSequenceValueAsync<T>(this DbContext db, string sequenceName, CancellationToken ct = default) where T : struct
```

Returns the next value of a sequence (`SELECT NEXT VALUE FOR`). Each call takes a new value, and two callers never get the same one.
A sequence value is not rolled back with a transaction, so gaps in the numbers are normal.

| Parameter | Meaning |
|---|---|
| `T` | The type the value is read as. It must match the sequence: a `bigint` sequence is read as `long`. |
| `sequenceName` | `Name` or `Schema.Name`. Letters (ASCII), digits and underscores only; the first character is not a digit; each part is at most 128 characters. |

The name is checked, then bracket-quoted (`[Schema].[Name]`). It is never written into the statement as given, so a name cannot change
the statement. The statement is only enumerated, never composed into a larger query: SQL Server refuses `NEXT VALUE FOR` inside a
subquery.

**Ways it fails:**

| Case | Exception |
|---|---|
| `db` or `sequenceName` is null | `ArgumentNullException` |
| `sequenceName` is empty, has more than two parts, or has a part that is not a plain identifier | `ArgumentException` (`sequenceName`). No SQL is sent. |
| The sequence does not exist, or the server refuses the read | `SqlException` (for example number 208, invalid object name), unchanged. |
| The read returns no row | `InvalidOperationException`. A sequence always returns one value, so this is a fault. |
| The value does not fit `T` | The provider's conversion exception. |

## CollationCheck and CollationResult

```csharp
public enum CollationResult { NotRelational, CaseInsensitive, CaseSensitive, Unverified }
public static class CollationCheck
```

An app that matches identifiers with a case-insensitive comparer relies on the database comparing text without case. On a
case-sensitive database, identifiers that differ only by case stop matching, and nothing says why. The check asks the database.

| Member | Meaning |
|---|---|
| `Query` | The probe: `SELECT CASE WHEN N'a' = N'A' THEN 1 ELSE 0 END AS [Value]`. |
| `DefaultFailureMessage` | The message `EnsureCaseInsensitiveAsync` throws when the app gives none. |
| `Evaluate(int? queryResult)` | `CaseInsensitive` for the answer 1. Anything else, including no answer, is `CaseSensitive`. |
| `CheckAsync(DbContext db, ILogger? logger = null, CancellationToken ct = default)` | Runs the probe and classifies the answer. |
| `EnsureCaseInsensitiveAsync(DbContext db, ILogger? logger = null, string? failureMessage = null, CancellationToken ct = default)` | Throws when the answer is `CaseSensitive`. |

| Result | Meaning |
|---|---|
| `CaseInsensitive` | The probe answered 1. |
| `CaseSensitive` | The probe answered 0, another value, or no row. |
| `NotRelational` | The provider has no collations (for example the in-memory provider). Nothing was checked. |
| `Unverified` | The probe could not run, for example because the database is unreachable or does not exist. A connectivity fault, not a finding. Logged as a warning when a logger is given. |

**Ways it fails:**

| Case | What happens |
|---|---|
| `db` is null | `ArgumentNullException`. |
| The context has no provider configured | EF Core's `InvalidOperationException` is thrown. It is a configuration error and is not classified. |
| The probe cannot run | `Unverified`, and `EnsureCaseInsensitiveAsync` does not throw. A startup check continues, and the readiness check reports the database. |
| The database compares text with regard to case | `CaseSensitive`, and `EnsureCaseInsensitiveAsync` throws `InvalidOperationException` with `failureMessage`, or with `DefaultFailureMessage` when that is null. |
| `ct` is cancelled | `OperationCanceledException`, not classified. |

## ConnectionStringCheck and ConnectionStringProblem

```csharp
public enum ConnectionStringProblem { None, LocalHost, LocalDb, IntegratedSecurity, Unreadable }
public static class ConnectionStringCheck
```

Refuses a developer database outside Development. A connection string that names this computer, a LocalDB instance, or Windows
authentication, in a deployed app, fails at its first query in a container and reads like a database outage. The check runs at
startup and names the setting.

`Classify` reads the parsed string. It does not search the text, so a database named `localhost` on a real server is not a developer
database. It never writes the string into a message, because the string can hold a password.

`Classify` returns the first problem found, in this order:

| Problem | When |
|---|---|
| `Unreadable` | The string does not parse, or its data source names no host (for example `Server=;` or an unclosed IPv6 bracket). |
| `LocalDb` | The server is `(localdb)\...`. |
| `LocalHost` | The host is `localhost`, a name under `.localhost`, `.`, `(local)`, a loopback address (127.x.x.x or `::1`), or the shared-memory protocol (`lpc:`). A `tcp:` or `np:` prefix is read past. |
| `IntegratedSecurity` | `Trusted_Connection=True`, `Integrated Security=True` or `SSPI`. |
| `None` | Anything else: a server on another computer, with a login that is not Windows authentication. |

| Member | Meaning |
|---|---|
| `Classify(string connectionString)` | The problem, as above. |
| `EnsureNotDeveloperDatabase(string connectionString, bool isDevelopment, string settingName)` | Returns when `isDevelopment` is true, or when `Classify` gives `None`. Otherwise throws. |

**Ways it fails:**

| Case | What happens |
|---|---|
| `connectionString` is null | `ArgumentNullException`. |
| `settingName` is null, or empty or white space | `ArgumentNullException` or `ArgumentException` (`settingName`). |
| `isDevelopment` is true | Nothing is refused, whatever the string. |
| The problem is `LocalHost`, `LocalDb`, `IntegratedSecurity` or `Unreadable` | `InvalidOperationException`. The message names `settingName` and the reason, and never the string. |

The apps' own messages can differ: an app can call `Classify` and throw its own text. A deployed string that is unreadable is refused
at startup. Before this check it started and failed later, so this is stricter for an unreadable string.

**What it does not detect.** It does not resolve a name through DNS or the hosts file, so a host name that points at this computer
is not caught. It does not compare the host with the machine's name. A `Trusted_Connection=False` string with a login is accepted.

## Running the SQL Server tests

Most tests create their own database on the server named in the environment variable `BALLAST_TEST_SQL`. The default is
`Server=localhost;Trusted_Connection=True;TrustServerCertificate=True`. Each database is named `Ballast_SqlServer_` and eight hex
digits, and is dropped when its test ends. When the server cannot be reached, a test is skipped (it shows as skipped, never as
passed). The tests that need no server (the model conventions, the connection string check, the name checks, the classifiers on
doubles, and the batch splitter) always run.
