# Ballast CI guide

Ballast's own CI and a reusable workflow for other repositories live in `.github/`. This guide covers the reusable workflow, its
two composite actions, the SQL Server service in Ballast's own CI, and the Semgrep rules in `semgrep/`.

**Status.** None of this has run on GitHub. The YAML files parse, every third-party action is pinned to a commit, and the two
composite action scripts were run locally against a closed port and a missing container, where they fail as they should. The
success path of `wait-for-url` was not run here. Treat the first GitHub run as the real test.

## Pins

Third-party actions are pinned to a full commit SHA, with the release in a comment. Local actions (`./.github/...`) are not
pinned. The four pins are the ones `ci.yml` already used: `actions/checkout`, `actions/setup-dotnet`, `actions/cache` and
`actions/upload-artifact`. The reusable workflow has no other third-party action. It installs no Node version itself: the GitHub
ubuntu image ships Node, and the `js` job uses whatever Node the image has.

## The reusable workflow

`.github/workflows/dotnet-ci.yml` is called with `workflow_call`:

```yaml
jobs:
  ci:
    uses: <owner>/<repo>/.github/workflows/dotnet-ci.yml@<ref>
    with:
      solution: MyApp.slnx
      sql-server: true
      sql-test-projects: |
        tests/MyApp.Jobs.Tests/MyApp.Jobs.Tests.csproj
```

### Inputs

| Input | Default | Meaning |
|---|---|---|
| `solution` | `Ballast.slnx` | The solution that restore, build, format, test and pack run on. |
| `dotnet-version` | `10.0.x` | The SDK that setup-dotnet installs. |
| `runner` | `ubuntu-24.04` | The runner image for every job. |
| `timeout-minutes` | `20` | The time limit of each job. |
| `workloads` | empty | Workload ids for `dotnet workload install`. Empty installs none. |
| `warnaserror` | `true` | Build with `-warnaserror`. |
| `format` | `true` | Run `dotnet format --verify-no-changes`. |
| `pack` | `true` | Pack into `pack-output` and upload it as `artifact-name`. |
| `pack-output` | `artifacts/packages` | Where the packages are written. |
| `artifact-name` | `packages` | The name of the uploaded artifact. |
| `sql-server` | `false` | Run the `sql-tests` job against a SQL Server service. |
| `sql-image` | `mcr.microsoft.com/mssql/server:2022-latest` | The SQL Server image for the service. |
| `sql-env-name` | `BALLAST_TEST_SQL` | The environment variable the SQL tests read. The job writes the connection string to it. |
| `sql-test-projects` | empty | Newline-separated test projects that need SQL. Each runs alone against the service. |
| `js-directories` | empty | Newline-separated directories with a `package.json`. Each runs `npm ci` and `npm test --if-present`. |

### Jobs

- **`build`** always runs: restore, build (with `-warnaserror` when `warnaserror` is true), format, test the solution, pack,
  upload. Its SQL-backed tests skip, since it has no server. This is the same as before.
- **`sql-tests`** runs only when `sql-server` is true and `sql-test-projects` is not empty. It has the SQL Server service
  container, waits for it, writes the connection string to `sql-env-name`, and runs each listed project with
  `dotnet test --project`. The first failing project stops the job and names itself in the log group.
- **`js`** runs only when `js-directories` is not empty.

The password in the service container is a throwaway for a container that exists only for that job, on the runner's own
localhost. It is not a secret and is not used anywhere else.

### Caveat for callers in another repository

The composite actions are referenced as `./.github/actions/wait-for-sql`. In a reusable workflow, a relative path resolves in
the caller's checkout, not in this repository. A caller in another repository must either copy `.github/actions/` into its own
repository, or the reusable workflow must reference the actions by their full path. This is not resolved yet. It is the first
thing to check on a caller's first run.

## The composite actions

### `wait-for-sql`

Waits until a SQL Server service container answers `SELECT 1` as `sa`, then returns. It finds the container by its image, so the
job must run that image as a service.

| Input | Required | Default | Meaning |
|---|---|---|---|
| `password` | yes | none | The `sa` password the container was started with. |
| `image` | no | `mcr.microsoft.com/mssql/server:2022-latest` | The image to find the container by. |
| `timeout-seconds` | no | `120` | How long to wait before failing. |

It checks with a query, not a connection, because the port opens before the server is ready. On timeout it prints the containers
it saw and exits 1.

### `wait-for-url`

Waits until a URL answers with the expected HTTP status, then returns. Use it for a server the job started in the background.

| Input | Required | Default | Meaning |
|---|---|---|---|
| `url` | yes | none | The address to call. |
| `expect-status` | no | `200` | The status that counts as an answer. |
| `timeout-seconds` | no | `120` | How long to wait before failing. |
| `insecure` | no | `false` | `true` accepts a self-signed certificate. Only for a development server. |

Each attempt has a 5-second limit, and attempts repeat every 2 seconds. On timeout it prints the last status and exits 1.

## Ballast's own CI

`.github/workflows/ci.yml` is one job, not a call to the reusable workflow. It keeps the same steps and adds three things:

- a SQL Server service on `localhost:1433`, so the SQL-backed tests (job lock, schema, SqlServer, Testing) run instead of skipping;
- the `BALLAST_TEST_SQL` environment variable, with a connection string to that service;
- the `wait-for-sql` step, before the restore.

## Semgrep rules

`semgrep/ballast-csharp.yml` and `semgrep/ballast-javascript.yml` are Ballast's generic rules. Every rule id starts with
`ballast-`, and no rule names an app or an app path. Their excludes are generic (`bin`, `obj`, `Migrations`, `node_modules`).

| File | Rules |
|---|---|
| `ballast-csharp.yml` | raw SQL with an interpolated string; raw SQL with a concatenated string; `new HttpClient`; token validation switched off; a secret in a log call; `Process.Start` with a non-literal; server certificate validation switched off. |
| `ballast-javascript.yml` | `innerHTML` or `outerHTML` set to a non-literal; `insertAdjacentHTML` with a non-literal; `document.write`; `eval` and `new Function`; `postMessage` with `*`; a token or password written to web storage. |

An app keeps its own overrides file beside its own rules. The app's file names the app's paths and adds to these rules; it does not
change their ids.

**Not run.** Semgrep is not installed here, and it was not installed for this work. The rules were parsed as YAML, and every regex
was checked at the line level against the test targets in `semgrep/tests/`. Each line marked `ruleid` is matched by its rule and each
line marked `ok` is not. That check is Python's regex engine, not Semgrep's, so it can differ. The test-file layout that
`semgrep --test` expects for a multi-rule file is **NEEDS VERIFICATION**: it is written as one target file per language, named after
the rule file.

Run them with Semgrep installed:

```
semgrep --config semgrep/ballast-csharp.yml --config semgrep/ballast-javascript.yml --metrics=off <path>
```

## The pack step

`dotnet pack Ballast.slnx` packs every library. `Ballast.Build` sets `IncludeSymbols` to `false`, because it has no assembly
and so no symbols; without that its empty symbols package made the pack fail (see `docs/Build.md`, "Packing"). The step is the
same in `ci.yml` and in the reusable `build` job.

## Publish phase: the wait actions by repository path and SHA

The reusable workflow references its composite actions as `./.github/actions/...`. That path resolves in the caller's checkout,
not in this repository, so it works only for callers that copy the actions, and only in this repository today. It is not changed
yet. When Ballast is on GitHub and a caller in another repository uses the reusable workflow, the publish step is:

1. Put the two actions in a published location: this repository, under `.github/actions/`, at a tagged commit.
2. In `.github/workflows/dotnet-ci.yml`, reference them by repository path and commit SHA, for example
   `uses: <owner>/ballast/.github/actions/wait-for-sql@<40-character SHA>`, with the release in a comment, the same way the
   third-party actions are pinned.
3. Test one caller run on GitHub before the tag is used. The success path of `wait-for-url` has not run anywhere yet.
