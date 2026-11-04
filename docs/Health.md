# Ballast.Health: usage guide

Ballast.Health gives an ASP.NET Core API its readiness. It maps a build route, a liveness route and a readiness route. It
provides the checks behind readiness: the database, the migration schema, startup gates and any check from outside. It runs
the warm-up steps, writes the readiness log and holds a new copy's port closed until it is ready. It targets ASP.NET Core 10
and uses Entity Framework Core 10 for the database and schema checks. Its namespace is `Ballast.Health`.

The package README has the install line and a runnable example. This guide covers every option and every public entry point,
and every way each one fails.

## How readiness decides

A platform moves traffic to a new copy of an API only while its readiness route answers 200. The readiness route answers from
its checks:

- Healthy or Degraded answers 200. Degraded is ready, but says why it is not fully Healthy.
- Unhealthy answers 503.

Only three things make a copy unready, and each is bounded:

1. The database has not answered since the copy started (Unhealthy at once), or it stopped answering after it had answered
   and the grace period has passed.
2. A migration the code ships with is not applied yet.
3. A startup gate has not been met before its own cap. After the cap a gate is Degraded, so the copy is ready anyway.

An upstream problem never makes a ready copy unready. A check the app adds decides for itself, and an uncapped gate is the one
exception that is never ready until its fact holds (see [Uncapped gates](#uncapped-gates)).

## Setting it up

Call the methods in this order. `AddBallastHealth` comes first, and `AddStartupHold` comes last, after every hosted service.

```csharp
builder.Services.AddBallastHealth(builder.Configuration, options => options.LogCategory = "Shop.Readiness");
builder.Services.AddDatabaseReadiness<ShopDbContext>();
builder.Services.AddSchemaReadiness<ShopDbContext>();
builder.Services.AddStartupGate("catalogue", sp => sp.GetRequiredService<CatalogueState>().Loaded, TimeSpan.FromSeconds(120));
builder.Services.AddWarmupGate(TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<IWarmupStep, CatalogueWarmupStep>();
builder.Services.AddReadinessCheck<JobLockCheck>("job-lock");   // a check from another library, or the app's own
// ... the app's other hosted services ...
builder.Services.AddStartupHold();

var app = builder.Build();
app.MapBallastHealth();
```

`AddBallastHealth` may be called once per application. Each of the database check and the schema check may be added once. A
second call of any of them is refused (see the tables below).

## Setup values: `HealthSetupOptions`

These are set in code, in the `configure` action of `AddBallastHealth`. They name things, so they are not bound from
configuration.

| Property | Default | Meaning |
|---|---|---|
| `SettingsSection` | `Health` | The configuration section that `HealthOptions` is bound from. |
| `BuiltAtMetadataKey` | `BuiltAtUtc` | The assembly metadata key the build writes its build time under. |
| `LogCategory` | `Ballast.Health` | The category the readiness log lines are written under. An app sets its own namespace here. |
| `BuildAssembly` | null: the entry assembly | The assembly whose metadata gives the build identity. A test host may name another. |

| Input | Result |
|---|---|
| `SettingsSection`, `BuiltAtMetadataKey` or `LogCategory` is null, empty or only white space | `ArgumentException`, named by the property, when `AddBallastHealth` runs |
| `BuildAssembly` is null | The entry assembly is used. Nothing fails. |

## Settings: `HealthOptions`

Bound from the section named by `SettingsSection` (`Health` by default). They are read at run time, so a change to the
configuration applies after a restart.

| Key | Default | Meaning |
|---|---|---|
| `DatabaseCacheSeconds` | 15 | How long one database answer is reused. Probes inside that window make no database call. |
| `DatabaseGraceSeconds` | 90 | How long the database may stop answering, after it has answered, before the copy is Unhealthy. |
| `SchemaRecheckSeconds` | 5 | How often a pending schema is checked again. A schema that is current is never checked again. |
| `CheckTimeoutSeconds` | 5 | The limit on one database or schema probe. A value below 1 is read as 1. |
| `StartupHoldMaxSeconds` | 0 | The longest the port stays closed while readiness is not yet passing, measured from process start. Zero or less turns the hold off. |

A value that is not a whole number makes the options fail when they are first read, which is when the first check or the hold
runs. The configuration binder then throws an `InvalidOperationException` that names the key.

The caps of the startup gates and the warm-up are not settings. Each gate and the warm-up gate are given their own cap in code
(see [Startup gates](#startup-gates)). The keys `RealtimeMaxWaitSeconds`, `BorderMaxWaitSeconds`, `WarmupMaxWaitSeconds` and
`JobLockGraceMinutes` are not read by this library. An app that set them keeps its own reading of them, and a job-lock check
reads its grace from its own library.

## Routes

`MapBallastHealth` maps the three routes below in one call. Each can also be mapped alone with `MapBuildInfo`,
`MapLiveness` and `MapReadiness`, which take the path as their only option. Every route is anonymous and is exempt from rate
limiting, because a probe is cheap by construction and a platform must always be able to ask.

| Route | Default path | Answer | Body |
|---|---|---|---|
| Build | `/health` | 200 while the process runs | `{"status":"healthy","timestamp":"…Z","commit":…,"builtAtUtc":…,"startedUtc":"…Z"}` |
| Liveness | `/health/live` | 200 while the process runs. No check is consulted. | Plain text `Healthy` (the default writer of the health checks). |
| Readiness | `/health/ready` | 200 when Healthy or Degraded, 503 when Unhealthy | `{"status":"Healthy","checks":{"database":"Healthy", …}}` |

- `timestamp` is the answer's instant, from the time provider. `startedUtc` is when the process started. `commit` and
  `builtAtUtc` are null when the build did not record them.
- The readiness body names each check that takes part and its status, and nothing else. It carries no description, exception,
  data or duration, because the route is anonymous. Its response has `Cache-Control: no-store`, so no cache answers for it.
- `MapReadiness` also starts the warm-up when the application has started. It does this whether or not the startup hold is on.
  Calling it twice does nothing extra.

| Input | Result |
|---|---|
| `endpoints` is null | `ArgumentNullException` |
| `path` is null, empty or only white space | `ArgumentException` |
| The same path is mapped twice | Each request to it fails with an ambiguous-match error from routing. Map each route once. |

## Checks

Every check below carries the readiness tag, `ReadinessEndpoint.ReadyTag` (the text `ready`). Each is a singleton, so a check that
keeps state keeps it between probes. Each has the timeout `HealthRegistration.CheckTimeout` (10 s): a check that runs longer is
reported as timed out, which the health service reports as Unhealthy.

### Database: `AddDatabaseReadiness<TContext>`

```csharp
AddDatabaseReadiness<TContext>(string name = "database")
```

Asks whether the context's database can be connected, in a fresh scope, at most once every `DatabaseCacheSeconds`. One ask runs
at a time, and the other probes wait for its answer. Each ask is cut off after `CheckTimeoutSeconds`.

| State | Result |
|---|---|
| The database has never answered since the copy started | Unhealthy at once |
| It answers | Healthy |
| It stopped answering after it had answered, for less than `DatabaseGraceSeconds` | Degraded: ready, and the description says so |
| It has not answered for `DatabaseGraceSeconds` or more | Unhealthy |

Ways it fails:

| Situation | Result |
|---|---|
| The ask throws, or is cut off by the timeout | The copy is treated as unreachable for that ask. The exception's type is logged, as a warning under `Ballast.Health.DatabaseHealthCheck`. No message reaches the body or the result. |
| `name` is null, empty or only white space | `ArgumentException` |
| A database check is already registered | `InvalidOperationException`. Register one per application, for one context. |

### Schema: `AddSchemaReadiness<TContext>`

```csharp
AddSchemaReadiness<TContext>(string name = "schema")
```

Reads the migrations the code ships with, and the migrations the database has applied, in a fresh scope.

| State | Result |
|---|---|
| A migration the code ships with is not applied | Unhealthy, with the count in the description. It is read again every `SchemaRecheckSeconds`. |
| Every migration the code ships with is applied | Healthy, for the rest of the process. The database is never asked again. |
| The database has applied migrations the code does not ship with (after a rollback) | Healthy. The code needs only what it ships with. |
| The history cannot be read, or the read is cut off by `CheckTimeoutSeconds` | Unhealthy, with "The migration history could not be read." It is read again after `SchemaRecheckSeconds`. The exception's type is logged as a warning under `Ballast.Health.SchemaHealthCheck`. |

Ways it fails:

| Situation | Result |
|---|---|
| `name` is null, empty or only white space | `ArgumentException` |
| A schema check is already registered | `InvalidOperationException` |

Concurrent probes wait for one read to finish, rather than each making its own. A probe that waits then gets that read's answer,
unless the recheck interval has passed.

### Startup gates

A gate is a fact that becomes true once during startup. Until it is met, a gate is Unhealthy, but only for its own cap, measured
from process start. After the cap it is Degraded, so the copy is ready anyway, and the readiness log gets one warning. Once met a gate
stays Healthy, whatever the fact does later.

```csharp
AddStartupGate(string name, Func<IServiceProvider, bool> met, TimeSpan maxWait)
AddWarmupGate(TimeSpan maxWait, string name = "warmup")
```

- `met` is asked on every probe until it first holds, and never after. It receives the root service provider, so it must read
  singletons only. A scoped service read from it would fail or outlive its scope.
- `AddWarmupGate` is met when every warm-up step has finished (see [Warm-up](#warm-up)). Its name is `warmup` unless the app names it.
- A `met` that throws makes the check fail, which the health service reports as Unhealthy. The text of the exception does not reach
  the body.

| Input | Result |
|---|---|
| `name` is null, empty or only white space | `ArgumentException` |
| `met` is null | `ArgumentNullException` |
| `maxWait` is negative | `ArgumentOutOfRangeException`. Zero is allowed: the gate is then Degraded at once. |
| A gate with the same name is already registered | `InvalidOperationException` |

The app reads its own configuration for a cap that operators may change. For example:

```csharp
var realtimeCap = TimeSpan.FromSeconds(builder.Configuration.GetValue("Health:RealtimeMaxWaitSeconds", 120));
builder.Services.AddStartupGate("realtime", sp => sp.GetRequiredService<Realtime>().FirstRoundDone, realtimeCap);
```

### Uncapped gates

```csharp
AddUncappedStartupGate(string name, Func<IServiceProvider, bool> met)
```

An uncapped gate is Unhealthy until `met` holds, however long that takes. It is never Degraded, and it never logs its warning. It is
a separate method on purpose: a capped gate is the default, and an uncapped one must be chosen.

Use it only for a fact that a deploy cannot do without, such as a startup task every request depends on, and only when that fact is
guaranteed to hold within the time the deploy allows. The risks are:

- If the fact never holds, this copy never becomes ready. The platform keeps the old copy serving, and the new copy takes no traffic.
- The startup hold gives up at `StartupHoldMaxSeconds`, and then the port opens while the gate is still Unhealthy. With the hold off,
  nothing ends the wait except the fact itself.
- A restart does not help when the fact depends on something outside the copy.

| Input | Result |
|---|---|
| `name` is null, empty or only white space | `ArgumentException` |
| `met` is null | `ArgumentNullException` |
| A gate with the same name is already registered | `InvalidOperationException` |

### Checks from outside

```csharp
AddReadinessCheck<THealthCheck>(string name) where THealthCheck : class, IHealthCheck
```

Adds any health check to readiness. It is a singleton, and it carries the readiness tag. So it takes part in the readiness route, in
the readiness log and in the startup hold, exactly as the checks above do. A check that reports Unhealthy or throws holds the copy
unready, and its name appears in the body. Another library adds its own check the same way (for example, a job-lock check).

| Input | Result |
|---|---|
| `name` is null, empty or only white space | `ArgumentException` |
| Two checks have one name | Both are registered. The health service refuses the pair with an `ArgumentException` when the checks first run, which is the first probe or the start of the hold. |

## Warm-up

A warm-up step does work a copy needs before it serves riders, such as filling a cache. The steps are registered as
`IWarmupStep` services, with `AddSingleton<IWarmupStep, TStep>()`.

```csharp
public interface IWarmupStep
{
    string Name { get; }
    Task RunAsync(IServiceProvider services, CancellationToken ct);
}
```

- The steps run one after another, in the order they were registered, each in a scope of its own. They run on a background thread,
  so the copy is not held up. The warm-up is not a hosted service, so it does not change the order of the app's hosted services.
- It starts when the startup hold starts, if the hold is on. Otherwise it starts when the application has started
  (`MapReadiness` registers that). Starting it again does nothing.
- A step that throws is logged as a warning with the exception, and the next step runs. The copy is ready anyway, because a cold
  cache is slow, not wrong.
- A step that never returns keeps the warm-up gate Unhealthy until that gate's cap, and then the gate is Degraded. A step should bound
  its own time.
- When the host stops, the steps stop, and `IsComplete` becomes true, so the gate does not wait for work that will not run.
- Each step is timed and logged as `Warm-up step {Name} finished in N s`. The whole run is logged as `Warm-up finished in N s`.

`StartupWarmup` is the runner. Its state is `IsComplete`, which is true once every step has finished or the host has stopped. `Begin`
starts the steps, and a second call does nothing.

## Startup hold

```csharp
AddStartupHold()
```

A hosted service that keeps the port closed while readiness is not yet passing. The port opens only after the last hosted service
has started, so the hold must be registered last, after every hosted service and the app's own services. The platform's start check
then keeps the old copy serving until this one is ready.

- The hold asks the readiness checks, the ones with the readiness tag, every 2 seconds. It returns as soon as the result is not
  Unhealthy, which means Healthy or Degraded. It never waits on a check without the tag.
- It returns at `StartupHoldMaxSeconds` from process start, even while readiness is Unhealthy. The copy may then be served cold.
- With `StartupHoldMaxSeconds` at zero, the default, it returns at once and does not start the warm-up.

| Log line | Level | When |
|---|---|---|
| `Startup hold: port opens after N s, readiness Healthy` (or `Degraded`) | Information | Readiness is not Unhealthy |
| `Startup hold: port opens at the N s cap while readiness is Unhealthy; the copy may be served cold` | Warning | The cap is reached |
| `Startup hold: the host stopped before readiness passed` | Information | The host stops first |
| `Startup hold failed; the port opens now, with readiness not confirmed` | Error | An unexpected exception. The hold never throws. |

| Input | Result |
|---|---|
| `AddStartupHold` is called on services without `AddBallastHealth` | The host fails to start, with an error that names the missing service |
| `services` is null | `ArgumentNullException` |

## Build info

```csharp
BuildInfoReader.Read(Assembly assembly, string builtAtKey = "BuiltAtUtc")
BuildInfoReader.Parse(string? informationalVersion, string? builtAtRaw)
```

`Read` returns a `BuildInfo` with two fields, both null when the build did not record them.

- **Commit.** The text after the first `+` of the informational version, when that text is a plain hexadecimal sha of 7 to 64
  characters. It is returned in lowercase. Any other suffix, a branch name or an empty value gives null.
- **Build time.** The metadata value under `builtAtKey`, read as a date and time (a value with no zone is taken as UTC), converted to
  UTC and written as `yyyy-MM-ddTHH:mm:ssZ`. A value that cannot be read gives null.

| Input | Result |
|---|---|
| `assembly` is null | `ArgumentNullException` |
| `builtAtKey` is null, empty or only white space | `ArgumentException` |
| Anything else | Nothing throws. A missing or unreadable value is null, never a guess. |

`Parse` is the same reading from two raw values, for tests. `HealthBody.Create(build, startedUtc, utcNow)` builds the body of the build
route.

## The readiness log

Every line is written under `LogCategory`. The readiness log says what readiness did.

| Line | Level | When |
|---|---|---|
| `Build: commit …, built …; started …` | Information | Once, when the warm-up begins |
| `Not ready, waiting on: {names}` | Information | Before the first ready answer, at most every 15 s |
| `Ready after N s (degraded: {names})` | Information | The first ready answer |
| `Not ready any more, after being ready: {names}` | Information | The first unready answer after being ready |
| `Ready again (degraded: {names})` | Information | The first ready answer after that |
| `Readiness probe: user agent …, scheme …, X-Forwarded-Proto …, answered N` | Information | For each of the first five distinct probes. The user agent is cut at 200 characters. |
| `Startup gate {name} was not met after N s; reporting ready anyway` | Warning | Once per gate, when its cap passes |

Names are the check names, in order.

## Types

Public types, for reference. Each is documented in the code.

| Type | Kind | Purpose |
|---|---|---|
| `HealthRegistration` | static class | The registration methods above; `CheckTimeout` |
| `HealthEndpoints` | static class | `MapBallastHealth`, `MapBuildInfo`, `MapLiveness`, `MapReadiness` |
| `HealthOptions` | class | The settings; `DefaultSectionName` |
| `HealthSetupOptions` | class | The setup values; `DefaultLogCategory` |
| `ReadinessEndpoint` | static class | `ReadyTag`, and `Options()` for the readiness route |
| `BuildInfo` | record | The commit and the build time |
| `BuildInfoReader` | static class | `Read`, `Parse`, `DefaultBuiltAtKey` |
| `HealthBody` | record | The body of the build route; `Create` |
| `HealthStart` | record | When the process started |
| `ReadinessLog` | class | The readiness log |
| `DatabaseHealthCheck` | class | The database check |
| `IDatabaseReachability`, `EfDatabaseReachability<TContext>` | interface, class | Whether the database answers |
| `SchemaHealthCheck` | class | The schema check |
| `IMigrationSource`, `EfMigrationSource<TContext>`, `MigrationReadout` | interface, class, record | The two migration lists |
| `StartupGateHealthCheck` | class | A capped gate. `AddStartupGate` builds one; an app may also construct or subclass it. |
| `StartupWarmup`, `IWarmupStep` | class, interface | The warm-up runner and its steps |
| `StartupHold` | class | The hosted hold |

`HealthStart` is taken when `AddBallastHealth` runs, which is the earliest point the application's own code runs. Every cap is measured
from it.

## Not in this library

- The job-lock check. It needs the job lock, and it joins readiness through `AddReadinessCheck` from its own library.
- Telemetry, authentication and the deploy script. They are the app's own.
