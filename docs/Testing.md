# Ballast.Testing

Test plumbing for xUnit v3 projects. It is a test-only package: a test project references it, and nothing that ships does.
It holds the SQL Server test databases that several runs can share, the captured logs and scripted HTTP fakes that keep a test off
the network, the test hosts, and the source guard kit: the scans that fail the build when a banned pattern appears, with their
allow-lists and a self-test that proves each scan can see what it guards against.

Install:

```
dotnet add package Ballast.Testing --prerelease
```

The package depends on xUnit's library packages (`xunit.v3.assert`, `xunit.v3.extensibility.core`), on ASP.NET Core's test host
(`Microsoft.AspNetCore.Mvc.Testing`), on SqlClient, on Entity Framework Core's relational package (the migration guard reads
migration operations, and the model check asks EF for pending changes) and on `Microsoft.Extensions.TimeProvider.Testing`. A test project also references `xunit.v3`, which is the
runner.

## Setup in a test project

Two things a test assembly writes itself. Neither can come from a library: xUnit reads assembly attributes from the test assembly.

1. The assembly fixture, once, if the project uses the SQL test databases:

   ```csharp
   [assembly: AssemblyFixture(typeof(Ballast.Testing.Sql.TestDatabaseRun))]
   ```

   It sweeps the databases a killed run left behind, and drops this run's own at the end.

2. Optional: the settings, before the first database is named, when the defaults do not fit:

   ```csharp
   TestDatabase.Configure(new TestDatabaseOptions { ConnectionVariable = "TESTS_SQL_CONNECTION", NameMarker = "_gtt" });
   ```

   `Configure` is called once, from the test assembly's fixture or its first test. The settings are fixed the first time a name is
   read, and `Configure` after that throws.

Clocks come from `Microsoft.Extensions.TimeProvider.Testing`: use its `FakeTimeProvider`. This library does not ship its own.

## SQL Server test databases

### `TestDatabaseOptions`

| Property | Default | Meaning |
|---|---|---|
| `ConnectionVariable` | `BALLAST_TEST_SQL` | The environment variable that holds the base connection string. Any database name in it is replaced. |
| `DefaultConnection` | `Server=localhost;Trusted_Connection=True;TrustServerCertificate=True` | Used when the variable is unset or blank. |
| `NameMarker` | `_btt` | Between a database's purpose and its run id. An underscore and 1 to 16 letters. |
| `StaleAfter` | 24 hours | A marked database from another run is a leftover once it is at least this old by the server's clock. Above zero. |

### `TestDatabase` (static)

| Member | What it does | How it fails |
|---|---|---|
| `Configure(options)` | Sets the settings, once, before the first use. | `ArgumentNullException` for null. `ArgumentException` for a blank variable name or default connection, a marker that is not an underscore and letters, or a non-positive age. `InvalidOperationException` when the settings were already fixed. |
| `NameMarker`, `Server`, `Master` | The marker in use; the base connection string; the connection string to `master`. Each fixes the settings. | Never fails. |
| `NewName(purpose)` | `{purpose}{marker}{RunId}_{n}`. The purpose is letters and digits, starting with a letter, at most 64 characters. | `ArgumentException` for any other purpose, including null. |
| `RunId` | Twelve lowercase hex characters, one per test process. | Never fails. |
| `ConnectionStringFor(name)` | The connection string to a database this run named. | `ArgumentException` for a name this run did not create. |
| `CreateEmptyAsync(name, collation?, ct)` | Creates an empty database named by `NewName`. The collation is a plain name (letters, digits and underscores). | `ArgumentException` for a name not from this run or a collation that is not a plain name. A server error from `CREATE DATABASE` is thrown as SqlClient throws it. |
| `DropAsync(name, ct)` | Drops a database this run named. A database that does not exist is left alone. | `ArgumentException` for a name not from this run. |
| `UnreachableReasonAsync(ct)` | Probes the server with a 3-second timeout and no pooling. Null when it answers; otherwise the reason, which names the variable to set. | Never throws: the failure is the answer. |

Names carry the purpose so a reader sees what a leftover was for. A fixed name such as `ShopDB` is refused by every call that
takes a name, so one run can never drop a database another run is using.

### `SqlTestDatabase`

```csharp
await using var database = await SqlTestDatabase.CreateAsync("Orders", collation: null, ct);
```

`CreateAsync(purpose, collation?, ct)` probes the server first. When the server cannot be reached it calls `Assert.Skip` with
`SQL Server not reachable: ...`, so the test shows as skipped, never as passed. Otherwise it creates the database, and the
`ConnectionString` and `Name` properties give it. `DisposeAsync` drops it, closing pooled connections to it first.

Failure: a skipped test when the server is unreachable; a thrown SqlClient exception when the create or the drop fails; the drop
throws, so a database that was not dropped is seen.

### `TestDatabaseRun` (the assembly fixture)

`InitializeAsync` drops every database of another run that is at least `StaleAfter` old. `DisposeAsync` drops this run's remaining
databases. Neither throws. When the server cannot be reached, or one drop fails, a line starting `[TestDatabase]` goes to standard
error and the run carries on.

### `SqlExceptions.Create(params int[] numbers)`

A `SqlException` whose errors carry the given numbers in order, so `Number` is the first. It goes through SqlClient's internal
constructors. If a SqlClient version renames them, it throws `InvalidOperationException` naming the constructor it could not find.

## Fakes

### `CapturedLogs`

An `ILoggerProvider` that keeps every entry: `Records` is a copy, in order, of `LogRecord(Category, Level, Message, Exception,
Scopes)`. The scopes are the states open when the entry was written, outermost first. Dispose does nothing, so the entries stay
readable after a host is disposed.

### `ScriptedHttpHandler`

An `HttpMessageHandler` that answers from a function, and records every request before it answers:

- `new ScriptedHttpHandler(request => response)`; `ScriptedHttpHandler.FromResponses(params responses)` answers in order.
- `Requests` (a copy of `SentRequest(Method, Url, Authorization, ContentType, Body)`) and `RequestedUrls`.
- `ThrowOnSend`: every send throws this instead of answering. `CancelOnSend`: every send cancels this source first.

Failures: a request after the last queued response throws `InvalidOperationException`, so a test that sends too many requests fails
instead of waiting. A null function is an `ArgumentNullException`.

### `StubHttpClientFactory(handler)`

An `IHttpClientFactory` whose every client uses the one handler. The name is ignored. The handler is not disposed with the client.

### `HttpAnswers`

`Text(status, body)`, `Ok(bytes)`, `Streamed(text)`, `RedirectTo(location, status = 301)`, `WithStatus(status, retryAfter?)`. Each
returns a new response.

### `LoopbackServer`

A real HTTP/1.1 server on `127.0.0.1` with a free port. `new LoopbackServer((method, path) => (status, extraHeaders, body))`.
`Port`, `BaseAddress` (`http://127.0.0.1:{port}`) and `Paths` (the requested paths, in order). Each answer closes the connection.
`DisposeAsync` stops the listener. A null function is an `ArgumentNullException`.

### `EnvironmentVariableScope.Set(name, value)`

Sets a process environment variable and returns a disposable that puts the old value back (or removes the variable when it was not
set). Environment variables are process-wide: do not run two tests that use one name at the same time. A blank name is an
`ArgumentException`. A second `Dispose` changes nothing.

## Hosts

### `InMemoryApp.StartAsync(configure, pipeline, environmentName = "Production")`

Builds a `WebApplication` on the test server, with its logs going to a `CapturedLogs` (Debug and above). `App`, `Client` and `Logs`
are on the result, and `DisposeAsync` stops the app. Null delegates are an `ArgumentNullException`.

### `TestApiFactory<TEntryPoint>`

A `WebApplicationFactory` base for an API under test. A derived class fills `Settings` (configuration keys) in its constructor, and
overrides:

- `EnvironmentName` (default `Development`);
- `RemoveHostedServices` (default true: the API's background services are removed so they do not run against the test database);
- `PrepareAsync(ct)` and `TeardownAsync(ct)`, which create and drop its database. They run with `CancellationToken.None`.

`DisposeAsync` stops and disposes the host first, then runs `TeardownAsync`, so a hosted service that still uses the database has stopped before the database is dropped. Teardown runs even when the host fails to dispose.

`CreateAnonymousClient()` and `CreateBearerClientAsync(loginPath, credentials, tokenProperty = "accessToken")` log in over HTTP, so
a test's token is one the login endpoint issues. The login fails with `InvalidOperationException` naming the status and the body
when it is refused, or when the answer has no token property.

## The guard kit

A guard is a test that reads the repository and fails when a banned pattern is there. The kit gives each guard the same parts.

### `RepoRoot.Find(marker, startDirectory?)`

Walks up from the start (default: the test assembly's folder) and returns the first folder that holds the marker, a file or folder
name such as `Ballast.slnx`. Fails with `DirectoryNotFoundException` when no folder up to the drive root holds it.

### `SourceScan`

- `Files(root, extensions, skippedDirectories?, maxBytes = 3 MB)`: the files under the root with those extensions, skipping any
  path that has a skipped folder in it (default `bin`, `obj`, `node_modules`, `.vs`) and any file over `maxBytes` (skipped, not
  truncated). Returns `SourceFile(RelativePath, FullPath)`. A missing root is a `DirectoryNotFoundException`, thrown at the call.
- `StripCSharpComments(source)`: the code without its `//` and `/* */` comments. It reads string, verbatim (`@"..."`), raw
  (`"""..."""`), interpolated and character literals whole, so a `//` in a URL is kept. Every line break is kept, including those
  inside a block comment, so a line number in the result is the source's own.

  Limitation: an interpolation hole that contains a string literal (`$"{a ?? "b"}"`) ends the outer string at the inner quote, so
  text after it in that literal is read as code. A comment there counts as code, which makes the guard report it for a reader to
  check. It never hides a violation.

### `AllowList.Problems(listName, entries, findings, minimumReasonLength = 20)`

Every entry is an `AllowEntry(Key, Reason)`. A finding is excused by an entry with the same key, when the entry:

- has a key;
- is listed once;
- has a reason of at least `minimumReasonLength` characters after trimming.

An entry that breaks a rule excuses nothing, so its finding fails too. An entry that matches no finding is stale and fails. The
result is the list of problem messages, empty when the list is clean. `minimumReasonLength` below 1 is an
`ArgumentOutOfRangeException`.

### `GuardSelfTest.AssertCatches(scan, planted, expectedFragment)` and `AssertPasses(scan, clean)`

A guard plants a violation in a string it writes and checks that the scan reports it (`AssertCatches`), and reports nothing for a
correct source (`AssertPasses`). A scan that finds nothing in the repository because its pattern is broken fails here instead.

### `ClockGuard`

The server-side time-zone rule: `ServerClockPattern` matches the machine's own clock reads (`DateTime.Now`, `DateTime.Today`,
`DateTimeOffset.Now`, `DateTimeOffset.Today`, `.ToLocalTime()`, `TimeZoneInfo.Local`). `Findings(path, source, banned = null)` returns
`path:line: code` for each match after comments are removed. The app keeps its folder list and its reviewed exceptions.

### Migration safety

`MigrationSafetyRules.Evaluate(operations)` flags, in a migration's `Up`: dropped tables and columns, renamed tables and columns,
deleted data, a column made required, a column made shorter or changed in type, a required column added without a default, and raw
SQL that drops, deletes, truncates, renames (`sp_rename`) or makes a column `NOT NULL`. `ON DELETE CASCADE` is not a delete. The
rule names are in `MigrationSafetyRules.RuleNames`. A rule is a tripwire, not a proof: the way past it is an additive change, or a
reviewed allow-list entry.

`MigrationGuard`:

- `Discover(contextType, provider = SqlServer)`: every migration of the context's assembly, with its violations under the rules.
  It builds each migration for the provider, and runs nothing.
- `Failures(migrations, cutoffId, allowList, advice = DefaultAdvice, minimumReasonLength = 20)`: the failure lines. A migration
  after the cutoff must pass, or be excused by a valid `MigrationAllowEntry(MigrationId, Rules, Reason)`. A migration at or before
  the cutoff is grandfathered.

The lines are a contract. Every violation starts `MIGRATION-GUARD {id}: {Rule}: {detail}. {advice}`, and every allow-list problem
starts `MIGRATION-ALLOW-LIST {id}:`. The deploy script (`Ballast.Deploy`) reads every line that starts with either marker, so their
text must not change. The exact problem texts are in the library's tests:

- no such migration; grandfathered, so the entry excuses nothing; the reason must be at least N characters; names no rules;
  stale, the migration has no `{Rule}` violation to excuse.

### `WritablePathGuard` (`Ballast.Testing.Guards.WritablePath`)

The writable-path rule. Server code writes only under its data root, never under the content root or the folder the app was started from,
because the content root is read-only once an app runs from its package. Every read of either sits on an allow-list line that says why it
is safe.

| `WritablePathOptions` property | Default | Meaning |
|---|---|---|
| `ServerTrees` | required | Folders under the root that hold server code. At least one. |
| `SkippedDirectories` | `bin`, `obj`, `Migrations`, `CompiledModels`, `publish` | Folder names skipped anywhere in a tree. |
| `RootUse` | `ContentRootPath`, `WebRootPath`, `AppContext.BaseDirectory`, `Directory.GetCurrentDirectory` | The reads the rule names. |
| `RetiredNames` | none | Names that must not appear in server code, comments included. |
| `ListName` | `WRITABLE-PATH-ALLOW-LIST` | Starts each problem message. |
| `Advice` | `Write under the data root instead, or add an entry with the reason the read is safe.` | Ends each unexcused read's message. |

| Member | What it does | How it fails |
|---|---|---|
| `ServerSources(root, options)` | Reads the `.cs` files of the trees as `(path, text)`, with paths from the root. | `ArgumentException` for no tree; `DirectoryNotFoundException` for a missing tree. |
| `Problems(sources, options, allowList)` | Each unexcused read, each invalid entry and each stale entry, as one line. Empty when the rule holds. | Never throws for a finding. |
| `RetiredNameProblems(sources, options)` | One line per file that still contains a retired name. The text is read raw. | Never throws for a finding. |

The allow-list key is `path|line fragment`, split at the first bar: the file, and a text the read line must contain. An entry excuses a read
only when it names that file and the read's line contains its fragment. An entry that excuses no read is stale.

```csharp
[Fact]
public void Server_code_reads_the_content_root_only_where_the_allow_list_says_so()
{
    var options = new WritablePathOptions { ServerTrees = ["ApiOne", "ApiTwo"] };
    var sources = WritablePathGuard.ServerSources(RepoRoot.Find("App.slnx"), options);

    Assert.Empty(WritablePathGuard.Problems(sources, options, ReviewedReads));
}
```

### `JobHolderGuard` (`Ballast.Testing.Guards.JobHolder`)

The job-lock rules. Background work that writes runs in one copy of an app at a time, the copy that holds the lock. These checks keep that true
as the code changes. Each hosted service is classified as `JobClass.HolderOnly` (registered with the leader method), `JobClass.EveryCopy`
(registered with the plain method) or `JobClass.Mixed` (registered plain, and it takes the job leadership to tell the two apart).

| `JobHolderOptions` property | Default | Meaning |
|---|---|---|
| `HostedServiceAssemblies` | required | The assemblies whose hosted services are classified. |
| `Classification` | required | Each service's type name and its class. |
| `RegistrationSources` | required | For each assembly, the text of each source that registers its services. |
| `LeaderRegistration`, `PlainRegistration` | `AddLeaderHostedService`, `AddHostedService` | The registration methods. |
| `RequiredRegistrationText` | none | Text a named service's registration must contain, such as `once: true`. |
| `MixedCarriers`, `LeadershipType` | none | The type that takes the leadership for each mixed service, and the leadership interface. |
| `HolderAttribute`, `ControllerAssemblies`, `HolderActions` | required | The attribute, the controllers, and the actions it must mark (`Controller.Action`, or `Controller.*`). |
| `LockCall`, `ShutdownCall` | `AddJobLeadership(`, `ConfigureGracefulShutdown(` | The lock's registration and the shutdown call. |
| `PublishMethodPrefix`, `PublishRouteFragment`, `PublishActions` | `PublishHeldVersion`, `publish-anyway`, none | How a publish-anyway action is found, and the expected ones. |

| Member | What it checks |
|---|---|
| `ClassificationProblems(options)` | Every hosted service is classified, every class is one of the three, and every entry names a service. |
| `RegistrationProblems(options)` | Each service is registered once in each source that runs it, with the method its class needs, and with any required text. |
| `MixedLeadershipProblems(options)` | Each mixed service names a carrier, and each carrier takes the leadership in a constructor. Throws `ArgumentException` when a service is mixed and no leadership type is named. |
| `StartupProblems(project, startupSource, options, callsAfterLock?)` | The lock is registered, the shutdown call is made, no hosted service is registered before the lock, and each call in `callsAfterLock` follows it. |
| `HolderActionProblems(options)` | Every action that carries the attribute is listed, and every listed action carries it. |
| `PublishActionProblems(options)` | Every publish-anyway action carries the attribute, and the ones found are the expected ones. |
| `CSharpSources(root, folders)` and `FixedTmpWriterProblems(sources, expected)` | The files that write a path ending in `.tmp` are exactly the expected ones. Two copies of an app that share a fixed name tear each other's file. |

```csharp
[Fact]
public void Every_hosted_service_is_classified()
{
    Assert.Empty(JobHolderGuard.ClassificationProblems(JobOptions));
}

[Theory]
[InlineData("ApiOne")]
public void The_job_lock_is_registered_before_the_first_hosted_service(string project)
{
    var startup = File.ReadAllText(Path.Combine(RepoRoot.Find("App.slnx"), project, "Program.cs"));

    Assert.Empty(JobHolderGuard.StartupProblems(project, startup, JobOptions));
}
```

### `AdminJsEscapeGuard` (`Ballast.Testing.Guards.AdminJs`)

The admin-page escaping rule. An admin page builds HTML in template literals and assigns it to `innerHTML`. A bare `${row.name}` in such a
template puts text from data into the page unescaped, which is stored cross-site scripting. The scan flags a text-like member path
interpolated as it is into a template that builds HTML. A value wrapped in the file's escape helper is not a member path, so it is not
flagged.

| `AdminJsOptions` property | Default | Meaning |
|---|---|---|
| `Folders` | required | Folders under the root whose `.js` files are scanned. |
| `TextNames` | `name`, `title`, `message`, `text`, `description`, `label`, `headsign`, `reason`, `error`, `detail`, `note`, `comment`, `summary`, `value`, `url` | The last property names that count as text. |
| `MinimumFileCount` | 1 | The fewest scripts the scan must find. A wrong path fails instead of passing. |
| `ListName`, `Advice` | `ADMIN-JS-ALLOW-LIST`, the escape-helper advice | Wording of the messages. |

| Member | What it does | How it fails |
|---|---|---|
| `Scan(code, textNames?)` | The unescaped values of one file, each with its line and normalised expression. | `ArgumentException` for an empty name list. |
| `Sources(root, options)` | Reads the `.js` files of the folders. | `DirectoryNotFoundException` for a missing folder; `InvalidOperationException` when fewer than `MinimumFileCount` scripts are found. |
| `Problems(sources, options, allowList)` | Each unescaped value that no entry excuses, and each invalid or stale entry. | Never throws for a finding. |

The allow-list key is `path|expression`, with the expression written as `Scan` reports it.

```csharp
[Fact]
public void No_admin_template_interpolates_data_text_without_escaping_it()
{
    var options = new AdminJsOptions { Folders = ["ApiOne/wwwroot/admin"], MinimumFileCount = 20 };
    var sources = AdminJsEscapeGuard.Sources(RepoRoot.Find("App.slnx"), options);

    Assert.Empty(AdminJsEscapeGuard.Problems(sources, options, ReviewedValues));
}
```

### `TransitionGuard` (`Ballast.Testing.Guards.Transitions`)

The transition rule. A status changes only from a state the code names. Either the write is a conditional claim, whose `Where` names the
status it must have, or it sits behind a transition check. An unconditional `entity.Status = X` overwrites whatever a concurrent request
already moved the row to. Every status write is listed with its reason and its count, and a new write fails until someone has looked at it.

| `TransitionOptions` property | Default | Meaning |
|---|---|---|
| `Trees` | required | Folders under the root whose `.cs` files are read. |
| `SkippedDirectories` | `bin`, `obj`, `Migrations` | Folder names skipped anywhere in a tree. |
| `StateEnums` | required | The state-machine enums whose tracked writes are checked, as `x.Status = Enum.Value`. |
| `StatusProperty` | `Status` | The property a tracked write sets. |
| `SetBasedProperties` | `Status`, `Stage`, `UndoneAtUtc`, `EntriesJson`, `ReadingState` | The properties whose `SetProperty(x => x.Name, ...)` writes are checked. |
| `ListName`, `Advice` | `TRANSITION-ALLOW-LIST`, the claim advice | Wording of the messages. |

| Member | What it does |
|---|---|
| `Sources(root, options)` | Reads the `.cs` files of the trees. |
| `Find(code, options, relativePath = "")` | The status writes of one file, each with its line. A comment line (one that starts with `//` or `*`) is skipped. |
| `Problems(sources, options, allowList)` | Each write no entry lists, each entry whose count differs from the code, each invalid entry, and each stale entry. |

`TransitionAllowEntry(RelativePath, Line, Count, Reason)` is the entry. It is not a plain `AllowEntry`, because the count is part of the
rule: a second copy of a listed write is a new write until it is reviewed. The key the shared check uses is `path: line`.

### `EndpointAuthGuard` (`Ballast.Testing.Guards.EndpointAuth`)

The endpoint-authorization rules. Every controller action of an API resolves to an authorization, and a reviewed table names the value each
action must have. An action with no entry fails, an entry with no action is stale, and an action whose value changed fails with the old and
new values side by side. The resolver reads the attributes the framework reads, so it needs no host and no database.

The values are `EndpointAuthGuard.Anonymous`, `Authenticated`, `AppOrUser` (an anonymous action that carries the app-session marker),
`AppOrUserBlockedByAuthorize` (the marker on an action that is not anonymous, which is always a mistake), `Policy:name` (a sorted list of
policies and roles), and `Unprotected` (no attribute at all, which is never allowed).

| `EndpointAuthOptions` property | Default | Meaning |
|---|---|---|
| `AppSessionMarker` | `RequireAppSessionAttribute` | The marker's attribute name, matched by name. |
| `AppVariable` | `app` | The variable the startup code maps routes on. |
| `HealthCall` | `app.MapBallastHealth()` | The one health call the startup code must make. |

| Member | What it checks |
|---|---|
| `Discover(assembly, options?)` | Each public action with its route and its resolved value. `[NonAction]` and `[NonController]` are left out. |
| `ResolveAuth(controller, action, options?)` | The value one action resolves to. |
| `Compare(actual, expected, tableName)` | Unclassified, changed, stale and unprotected entries, one line each. |
| `UndefinedPolicyProblems(apiName, authValues, definedPolicies, exemptPolicies?)` | A policy named by a value that the API does not define, unless it is exempt. |
| `DuplicateProblems(actual)` | Two actions that share a key (an overload), or answer the same verb and route. |
| `RouteInventoryProblems(actual, auth, documentedRoutes, section)` | The routes that resolve to a value are exactly the documented ones. |
| `AppSessionBlockedProblems(actual)` | Actions with the marker that are not anonymous. |
| `MinimalRouteProblems(startupSource, options?)` | No route is mapped in the startup code, `HealthCall` is made once, and `.AllowAnonymous()` is not called there. |

The expected table is a map from key to value, not an allow-list: its entries carry no reason, so the shared allow-list rules do not apply.

```csharp
[Fact]
public void Every_action_of_the_api_has_its_reviewed_authorization()
{
    var actual = EndpointAuthGuard.Discover(typeof(OrdersController).Assembly);

    Assert.Empty(EndpointAuthGuard.Compare(actual, ExpectedAuthorization, nameof(ExpectedAuthorization)));
}
```

### `ModelMatchGuard` (`Ballast.Testing.Guards.ModelMatch`)

The model-matches-migrations rule. A DbContext's model must match its migrations, so a model change with no migration fails here by name,
not inside `Migrate()` at deploy time. The rule also forbids suppressing EF's pending-model warning, because a suppressed warning lets
`Migrate()` run over a model that no migration describes.

| `ModelMatchOptions` property | Default | Meaning |
|---|---|---|
| `Advice` | `Add a migration for the change.` | Ends the model's message. |
| `SkippedTopLevel` | `tests`, `docs`, `bin`, `obj`, `node_modules` | Top-level folders the suppression scan skips. Folders starting with a dot are skipped too. |

| Member | What it does |
|---|---|
| `Problems<TContext>(create, options?)` | One line when the context's model has changes no migration covers. The factory builds the context with provider options that name no reachable server; the check never connects, and disposes the context. |
| `SuppressionSources(root, options?)` | The non-test `.cs` files of the repository. |
| `SuppressionProblems(sources)` | One line per source that calls `ConfigureWarnings` or `Ignore` with `PendingModelChangesWarning`, outside a comment. |

The check needs the SQL Server provider's package in the test project, because `UseSqlServer` is what builds the model. The version comes
from the central package list.

```csharp
[Fact]
public void Model_matches_the_migrations()
{
    Assert.Empty(ModelMatchGuard.Problems(() => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=.;Database=unused").Options)));
}
```

## How a guard fails, in general

- A guard never passes on an empty scan without a self-test: each one plants a violation first.
- A finding is never silently dropped. It is excused by a reviewed entry with a reason, or it fails.
- An allow-list entry that no longer matches anything fails, so the list shrinks as the code is fixed.
- A guard that cannot read its inputs (a missing root, a missing marker, a missing folder, too few scripts) throws. It does not report "no findings".

## What the app keeps

The engines are in this library. The app keeps the values they are given: its folders, its classifications, its controller and startup
lists, its expected tables, its allow-lists with their reasons, and the one test method per rule that calls an engine. An engine's rule is
the same in every app. A rule an app wants to differ from needs a new option, not a copy of the engine.
