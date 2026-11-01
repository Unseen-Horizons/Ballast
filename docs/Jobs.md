# Ballast.Jobs: usage guide

Ballast.Jobs makes sure that background work that writes runs in one copy of an app at a time. During a deploy the old and the
new copy of an app run side by side for a while. This library holds one SQL Server application lock, on a connection of its own,
and only the copy that holds it runs the work. It also provides the hosted services that run only under that lock, the 409 that
an admin action gives a copy without the lock, and a readiness check that says whether a copy is a sensible part of the lock.
It targets ASP.NET Core 10 and Microsoft.Data.SqlClient. Its namespace is `Ballast.Jobs`.

The package README has the install line and a runnable example. This guide covers every option and every public entry point,
and every way each one fails.

## How the lock decides

Two copies of an app compete for one lock. The one that gets it is the holder, and the holder's time in that role is a term.

- A copy that does not hold the lock keeps asking for it. When the holder lets go, SQL Server grants it to the next asker.
- A term starts in this order:
  1. the lock is acquired and the copy's outcome becomes `Acquired`;
  2. the term-start hooks run (`IsHolder` is still false, and the heartbeat is already running);
  3. `IsHolder` turns true, and the term's cancellation token is handed to everything waiting in `WaitForLeadershipAsync`.
- A heartbeat runs for the whole term. Every `HeartbeatInterval` it asks SQL Server whether this session still holds the lock. If
  the answer is no, or the question cannot be answered, the term is lost (see [How it fails](#how-it-fails)).
- At shutdown `IsHolder` is cleared and the term's token is cancelled the moment the application starts stopping. Every
  holder-only worker starts to wind down together. The lock itself is released only when the lock's own hosted service stops,
  which the host does after every other hosted service has stopped. The next copy cannot take over while this copy still has
  work in flight.

Why the lock is held the way it is. The lock is held on its own connection, not a pooled one, so that closing the connection ends
the session and frees the lock, and a dropped connection is seen as a lost lock rather than a silent reconnect to a session that
holds nothing. The heartbeat asks SQL Server every interval instead of only checking that the connection is alive, so a lock that
disappears is seen within one interval, and a question that cannot be answered counts as a lost lock (the copy stops its work
rather than assume it still holds the lock). Some other SQL Server lock helpers share connections and report a loss only when the
connection itself dies; this library does not rely on that.

## Setting it up

Call the methods in this order. `AddJobLeadership` comes before any hosted service of the app, so that the lock starts first and
stops last.

```csharp
builder.Services.AddJobLeadership(builder.Configuration, "Shop:jobs", connectionString, setup =>
{
    setup.RequireJobHolder.Title = "This server copy is not running background jobs yet.";
});
builder.Services.ConfigureGracefulShutdown(builder.Configuration);
builder.Services.AddLeaderHostedService<OrderExpiryWorker>();
builder.Services.AddJobLockReadiness(builder.Configuration, "Health");   // with Ballast.Health's readiness, see below
// ... the app's other hosted services ...
```

The lock is off until `JobLeadership:Enabled` is `true`. With it off, this copy is always the holder, no connection is made and no
lock is taken, so Development and the tests behave as if no lock existed. Production turns it on.

Also set the container's stop limit on Azure App Service, which is outside the code: see
[the setup note](setup/app-service-stop-time.md).

## Setup values: `JobLeadershipSetup`

These are set in code, in the `configure` action of `AddJobLeadership`. They name things, so they are not bound from configuration.

| Property | Default | Meaning |
|---|---|---|
| `SettingsSection` | `JobLeadership` | The configuration section that `JobLeadershipOptions` is bound from. Must not be blank. |
| `ApplicationNameSuffix` | `-lock` | Added to the lock resource to make the lock connection's application name, so a database administrator can see which session holds the lock. May be empty, but not null. |
| `RequireJobHolder` | see below | The title and detail of the 409 answer. Both must not be blank. |

`RequireJobHolder` is a `RequireJobHolderOptions`, with these defaults:

| Property | Default |
|---|---|
| `Title` | `This server copy is not running background jobs yet.` |
| `Detail` | `A deploy is in progress and the previous copy has not finished stopping. Try again in a minute.` |

## Settings: `JobLeadershipOptions`

Bound from the section named by `SettingsSection` (`JobLeadership` by default).

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | `true` turns the lock on. Off, this copy is always the holder and no connection is made. |
| `ResourceName` | empty | When not blank, it replaces the resource the app passed to `AddJobLeadership`. Trimmed. |
| `LockTimeout` | 15 seconds | How long one `sp_getapplock` call waits for the holder to let go. |
| `HeartbeatInterval` | 10 seconds | How often the holder asks SQL Server whether it still holds the lock. |
| `BusyRetryDelay` | 5 seconds | The wait before asking again after another copy was found holding the lock. |
| `ErrorRetryDelay` | 15 seconds | The wait before asking again after an error (the database could not be reached, for example). |
| `ReleaseTimeout` | 5 seconds | How long the release at shutdown may take. Closing the connection frees the lock either way. |

Durations are bound as `TimeSpan` values (for example `00:00:10`).

## Registration: `AddJobLeadership`

```csharp
IServiceCollection AddJobLeadership(
    IConfiguration configuration, string resourceName, string connectionString, Action<JobLeadershipSetup>? configure = null)
```

- `resourceName` is the lock both copies compete for. It is required and has no default. `JobLeadership:ResourceName` replaces it
  when set. Use a name that is unique to the app's database, such as the app's name followed by `:jobs`.
- `connectionString` is the connection to the database that holds the lock. It is used only when the lock is on.
- With the lock off, the registration adds one `IJobLeadership`, `AlwaysHolderLeadership`, and no hosted service.
- With the lock on, it adds `JobLeadership` as the one `IJobLeadership`, and as a hosted service.
- In both cases it adds the `RequireJobHolderOptions` built from the setup, so that the 409 answer has its words.

The registration checks its values when it runs, and throws `ArgumentException` for a blank resource, a blank connection string, a
blank settings section, or a blank title or detail. It makes no connection. Settings are read when the registration runs.

## The shutdown wait: `ConfigureGracefulShutdown`

```csharp
IServiceCollection ConfigureGracefulShutdown(IConfiguration configuration, int defaultSeconds = 60)
```

It sets the host's shutdown timeout to `Hosting:ShutdownTimeoutSeconds`, or to `defaultSeconds` when the configuration does not set
it. The host's own default is 30 seconds; this registration raises it so that the workers of a stopping copy can finish. A value
below one second is read as one second. The container that runs the app must allow longer than this wait before it stops the
container, which is set outside the code (see [the setup note](setup/app-service-stop-time.md)).

## Leader-only workers: `AddLeaderHostedService`

```csharp
IServiceCollection AddLeaderHostedService<T>(
    Func<IServiceProvider, T>? factory = null, bool once = false, TimeSpan? stopBudget = null)
    where T : BackgroundService
```

It registers a worker that runs only while this copy holds the lock, in place of `AddHostedService<T>()`. The worker itself does not
change. The wrapper, `LeaderHostedService<T>`, works like this:

- It waits for leadership, then starts the worker with the term's token as the worker's stopping token.
- When the term ends, it tells the worker to stop and gives it `stopBudget` to unwind (45 seconds by default).
- No new term starts until the worker's task has really finished, so two runs of one worker never overlap.
- A worker that stopped because the term ended is started again, on the same instance, in the next term.
- A worker that finishes on its own (because it is switched off, or it is a one-shot) is not started again.
- With `once: true` the worker runs in at most one term, even when that term ends before it has finished.
- A fault in the worker is rethrown as the worker's own exception, so the host treats it as it would for a worker registered
  directly.

`factory` is for a worker that something else already registers as a singleton (a controller reads its counters, say). Without it
the worker is registered as a singleton here (`TryAddSingleton`). `stopBudget` must not be negative (`ArgumentOutOfRangeException`).

With the lock off, the worker runs at once and stops with the host.

## The 409 for admin actions: `RequireJobHolderAttribute`

```csharp
[RequireJobHolder]
public async Task<IActionResult> Export(...) { ... }
```

Put it on an action or a controller that starts background work: an import, an export, a run that the holder's workers would
otherwise do. It is an MVC resource filter, so it runs after authorization. A caller who is not signed in or not allowed still gets
401 or 403 first, and does not learn that a deploy is going on.

- If no `IJobLeadership` is registered, or this copy is the holder, the action runs as before.
- Otherwise the action does not run. The response is written as a problem (`application/problem+json; charset=utf-8`) with:
  - status `409`, and `type` `https://tools.ietf.org/html/rfc9110#section-15.5.10`;
  - `title` and `detail` from `RequireJobHolderOptions`;
  - `code` `NOT_JOB_HOLDER` (`RequireJobHolderAttribute.ErrorCode`), a public code that is never changed;
  - `instance` (the request path) and `traceId`;
  - the `X-Trace-Id` header, and `Retry-After: 30` (`RequireJobHolderAttribute.RetryAfterSeconds`).
- The 409 is the library's answer for "busy, try again". It keeps a deploy out of the 5xx alerts.

The attribute works for MVC actions and controllers only. It is not a filter for minimal API endpoints.

## Term hooks: `IJobTermHook`

```csharp
public interface IJobTermHook
{
    Task OnTermStartAsync(CancellationToken ct);
}
```

Register a hook as a singleton with `AddSingleton<IJobTermHook, THook>()`. Hooks run when this copy has taken the lock, before its
holder-only workers are released. The previous holder has finished stopping by then, so whatever it left behind can be dealt with
safely. Hooks run one after another, in registration order:

- `IsHolder` is still false, and the heartbeat is already running. A hook that takes minutes does not make the copy unhealthy
  (see the readiness check).
- A hook resolves what it needs when it is called, not in its constructor, because the services it reaches may themselves ask for
  `IJobLeadership`.
- A hook that throws is logged at Error, naming the hook's type, and the term goes on with the next hook.
- A hook that is cut short because the term ends stops the remaining hooks, without an error.

## The copy's view of the lock: `IJobLeadership`

| Member | Meaning |
|---|---|
| `Enabled` | False when the lock is off. |
| `IsHolder` | True while this copy may run holder-only work. Cleared at once when the application starts stopping. |
| `HolderSinceUtc` | When the current term began, or null when this copy is not the holder. |
| `LastAttemptUtc` | When the lock was last tried, or null before the first attempt. |
| `LastOutcome` | What the last attempt came to: a `JobLeadershipOutcome`. |
| `LastError` | The message of the last error or loss, or null. |
| `StartedUtc` | When the process began. |
| `WaitForLeadershipAsync(ct)` | Completes when this copy is the holder, with a token cancelled when its term ends. Completes at once when it already holds the lock. Ends with `OperationCanceledException` when `ct` is cancelled first. |
| `RunAsLeaderAsync(work, once, ct)` | Runs `work` while this copy is the holder, with the term's token. When the term ends before the work does, it waits for the next term and runs the work again, unless `once` is true. A piece of work that finishes on its own is not run again. |

`JobLeadershipOutcome` values:

| Value | Meaning |
|---|---|
| `NotStarted` | No attempt yet. |
| `Disabled` | The lock is off. |
| `Acquired` | This copy holds the lock, or is taking it (the hooks are running). |
| `HeldByOther` | Another copy holds it. Not a fault. |
| `Error` | The attempt failed. |
| `Lost` | This copy held the lock and found it gone. |
| `Released` | This copy let go of the lock at shutdown. |

`AlwaysHolderLeadership` is the `IJobLeadership` used with the lock off. It is always the holder: `IsHolder` is true,
`LastOutcome` is `Disabled`, `LastAttemptUtc` and `LastError` are null, its term is never cancelled, and `RunAsLeaderAsync`
runs the work with the host's token.

## The lock's session: `IAppLockSession` and `SqlAppLockSession`

`SqlAppLockSession` is the SQL Server session. It is built from the app's connection string with two changes: pooling is off and
connection retry is off. The application name is the lock resource plus the setup's suffix (`Shop:jobs-lock` by default).

- `OpenAsync` opens the connection.
- `TryAcquireAsync` calls `sp_getapplock` for an exclusive lock with the session as its owner. It waits up to the timeout on the
  server. Its command timeout is the timeout in seconds plus 15. Return codes: 0 (granted at once) and 1 (granted after waiting)
  are granted; -1 (still held by another session) is busy, and it returns false. Any other code (for example -2 cancelled, -3 a
  deadlock victim, -999 a call error) throws `InvalidOperationException`.
- `HoldsAsync` asks `APPLOCK_MODE` for the session, with a command timeout of 20 seconds, and is true only for `Exclusive`.
- `ReleaseAsync` calls `sp_releaseapplock` for the session, with a command timeout of 10 seconds.
- `DisposeAsync` closes the connection. With pooling off this ends the session, which frees any lock it still holds.

The lock is a row in no table. Nothing is written to the database.

## Readiness: `AddJobLockReadiness`

```csharp
IServiceCollection AddJobLockReadiness(IConfiguration configuration, string settingsSection, string checkName = "job-lock")
```

It adds the job-lock check to readiness under `checkName` (`job-lock` by default). The check carries Ballast.Health's readiness tag, so
it takes part in the readiness route, the readiness log and the startup hold like every other readiness check. It is visible on the
readiness route only where the app maps that route (Ballast.Health's `MapBallastHealth`).

`settingsSection` is the configuration section the grace is bound from. It is required, and the key is `JobLockGraceMinutes`, so an app
that keeps its readiness settings in a `Health` section passes `"Health"` and binds `Health:JobLockGraceMinutes`. The grace defaults to
15 minutes. The check uses the `TimeProvider` registered in the container, and registers `TimeProvider.System` when none is. Call it
once: a second call adds a second check registration under the same name, which the health checks refuse at runtime.

The check's rule matters most: a copy is never required to hold the lock, because the old copy stops only after the new one is ready.
A rule that needed the lock would deadlock every deploy.

| State of this copy | Result |
|---|---|
| The lock is off | Healthy |
| This copy holds the lock | Healthy |
| Another copy holds it (`HeldByOther`), which is a deploy's new copy waiting | Healthy, however long |
| Taking the lock (`Acquired`), including while the term-start hooks run | Healthy, however long |
| Stopping (`Released`) | Healthy |
| No job lock registered | Healthy |
| Anything else (`Error`, `Lost`, `NotStarted`) | A streak: Healthy under 5 minutes, Degraded from 5, Unhealthy at the grace |

The streak starts at process start when this copy has never been good, and at the first bad answer otherwise. Any good answer ends it.
The grace defaults to 15 minutes. A copy that has never held or waited for the lock is therefore Degraded from 5 minutes after start.

## Order

The lock must start before every other hosted service and stop after them. Two rules keep that order:

1. Call `AddJobLeadership` before the app's first hosted service is registered (`AddHostedService` or `AddLeaderHostedService`).
   Hosted services start in registration order and stop in reverse.
2. Call Ballast.Health's `AddStartupHold` last, after every hosted service, because the port opens only after the last hosted
   service has started.

The registration cannot see the hosted services the app registered before it, so it cannot check rule 1. The app keeps that order,
and a test of its own is the place to check it.

The lock's service does not hold up the host's start: it yields at once and connects in the background.

## Shutdown

When the host stops:

1. The application stopping signal clears `IsHolder`, cancels the term's token and sets the copy to stopping. Every holder-only worker
   begins to wind down at once.
2. The host stops the other hosted services. A leader-only worker gets its `stopBudget` to unwind.
3. The lock's service stops last. If its loop has finished, it releases the lock; if the loop is still running, it does not. Then it
   closes the connection, which frees the lock either way. The outcome becomes `Released`.

The shutdown wait (`ConfigureGracefulShutdown`) bounds the whole of this.

## How it fails

| Situation | What happens | Logged | Seen as |
|---|---|---|---|
| Lock off (the default) | No connection, no heartbeat, no 409 is ever given. | Nothing. | `Disabled`, Healthy. |
| The connection cannot be opened, or the session call throws | The attempt is recorded as `Error` with the message, and the copy waits `ErrorRetryDelay` before it asks again. | Warning: could not take the lock; trying again. | `Error`. Readiness counts a bad streak. |
| `sp_getapplock` returns something other than granted or busy (-2, -3, -999 and others) | The same as a failed attempt, with `InvalidOperationException` as the message. | As above. | `Error`. |
| Another copy holds the lock | Not a failure. The copy waits `BusyRetryDelay` and asks again. Each `sp_getapplock` call itself waits up to `LockTimeout`. | Information, once when the outcome changes to `HeldByOther`. | `HeldByOther`, Healthy. |
| The lock is granted while the application is stopping | No term starts and no hook runs. The session is kept, and `StopAsync` releases it. | Information. | `Acquired`, then `Released`. |
| A term-start hook throws | The term goes on with the next hook. | Error, naming the hook's type. | Unchanged. |
| The term ends while the hooks run | The remaining hooks are skipped. If the copy is stopping, the session is kept and released at shutdown; if the lock was lost, the session is closed and the copy asks again. | None for a stop. | `Acquired`, then `Lost` or `Released`. |
| The heartbeat's question returns "not held" | The term is lost. `IsHolder` clears and the token is cancelled at once, the session is closed (which frees any lock it still holds), and the copy asks again. | Warning: the term ended because the lock was lost. | `Lost`, with the reason in `LastError`. |
| The heartbeat's question throws (the connection dropped, or a timeout) | Treated as a lost lock. The reason is the exception's message. The same steps as above follow. | Warning: the heartbeat failed, then the loss. | `Lost`. |
| A callback on the term's token blocks | The loss is handled first: the term is cleared and the token is cancelled in the background. The loop is not held up by the callback. | Warning if the callback throws. | `Lost`. |
| Releasing the lock fails at shutdown | The connection is closed, which frees the lock. | Warning: releasing failed; closing the connection frees it. | `Released`. |
| The lock connection fails to close | The failure is ignored and logged at Debug. | Debug. | Unchanged. |
| A leader-only worker does not stop within `stopBudget` | It is not started again until its run has finished. The wrapper waits for it. | Error, naming the worker and the budget. | Unchanged. |
| A leader-only worker throws | The exception is rethrown from the wrapper. The host's own policy for a failed background service then applies. | Whatever the host logs. | The host's behaviour. |
| An admin action with `[RequireJobHolder]` runs on a copy that does not hold the lock | 409 `NOT_JOB_HOLDER`, `Retry-After: 30`, the action does not run. | None from the library. | A 409 answer. |
| No `IJobLeadership` is registered when `[RequireJobHolder]` runs | The action runs as before. | None. | The action's own answer. |
| The readiness check runs with no job lock registered | Healthy. | None. | Healthy. |
| Registration is given a blank value, a negative stop budget, or a null argument | `ArgumentException`, `ArgumentOutOfRangeException` or `ArgumentNullException`, at startup. | None. | A startup failure. |
| `Hosting:ShutdownTimeoutSeconds` is zero or below | Read as one second. | None. | One second. |

## Why the copy never fails a deploy

The readiness check never asks the copy to hold the lock. A copy that waits behind the holder is Healthy for as long as it waits. A
copy that has neither held nor waited for the lock for longer than the grace is Unhealthy, and only then.

## Packaging

Ballast.Jobs depends on Ballast.AspNetCore and Ballast.Health, and through Ballast.Health on Entity Framework Core. That is Ballast.Health's
own dependency for its database check, and an app that uses the lock gets it through this package.

## Not in this library

- The app's writing jobs. The library runs them only while the lock is held; it does not know what they write.
- What the app's other endpoints and caches do on a copy that does not hold the lock. The library gives leadership and the
  409; the app decides the rest.
- Sign-in, tokens and any authorization decision. `[RequireJobHolder]` runs after authorization and decides nothing about who may
  call an action.
