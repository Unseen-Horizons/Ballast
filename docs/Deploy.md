# Ballast.Deploy

Ballast.Deploy is a PowerShell 7 module that deploys an Azure App Service app from a pushed commit. For one run it:

1. checks the app's settings and refuses an app that cannot be deployed this way;
2. builds the commit in a clean git worktree and zips it;
3. applies the commit's EF Core migrations to Azure SQL, behind a migration-safety guard and a y/N prompt;
4. zip-deploys the commit;
5. waits until the app serves that commit and reports ready, on two polls in a row;
6. tags the deploy as `deploy/<app>/<date>-<N>` and pushes the tag.

A rollback is a deploy of an older deploy tag. The module names no app. An app describes its own placement in an `apps.psd1` file, which the module reads with `Import-PowerShellDataFile`.

## Requirements

- PowerShell 7.0 or later (`pwsh`). Windows PowerShell 5.1 is not supported.
- The Azure CLI (`az`), signed in with `az login`, with access to the subscriptions in the config.
- `git`, with the repository's `origin` remote. Only a commit that is already on `origin` can be deployed.
- The .NET SDK, and the EF Core tools restored in the repository (`dotnet tool restore`) when an app has migrations.

## Install and import

Until the module is published, import it from its folder:

```powershell
Import-Module ./deploy/Ballast.Deploy/Ballast.Deploy.psd1 -Force
```

The manifest's version is 0.1.0 with the prerelease label `preview2` (`PrivateData.PSData.Prerelease`). The module is versioned on its own; the Ballast libraries take their version from `Directory.Build.props`.

## The entry script

An app keeps a small script next to its config. The script reads the config, runs the deploy, and exits with its code:

```powershell
#Requires -Version 7.0
Import-Module (Join-Path $PSScriptRoot 'Ballast.Deploy/Ballast.Deploy.psd1') -Force
$config = Read-BallastConfig -Path (Join-Path $PSScriptRoot 'apps.psd1')
$result = Invoke-BallastDeploy -Config $config -App @('api') -RepoPath (Join-Path $PSScriptRoot '..')
exit $result.ExitCode
```

`Read-BallastConfig` throws on a bad config, before anything runs. Left unhandled in a script, that exits with code 1.

## The config: apps.psd1

A data file with two top-level keys: `Options` (optional) and `Apps` (required). Unknown keys are refused, so a typo never keeps a default.

```powershell
@{
    Options = @{
        TagDateFormat = 'yyyy-MM-dd'
    }
    Apps = @{
        api = @{
            Subscription            = 'Example Subscription'
            ResourceGroup           = 'example-rg'
            WebApp                  = 'example-api'
            HealthUrl               = 'https://example-api.example.net'
            Project                 = 'src/Example.Api/Example.Api.csproj'
            LegacyBuildTimeProperty = 'ExampleBuildTimeUtc'
            Migrations = @{
                DbContext          = 'ExampleDbContext'
                SqlServer          = 'example-sql'
                Guard              = @{ Project = 'tests/Example.Tests/Example.Tests.csproj'; Filter = '*MigrationSafetyGuardTests' }
                Environment        = @{ ASPNETCORE_ENVIRONMENT = 'Production' }
                ThrowawayVariables = @('Example__SigningKey')
            }
        }
    }
}
```

### App keys

| Key | Required | Meaning |
|---|---|---|
| `Subscription` | yes | The subscription's name or id. A name must match exactly one enabled subscription. Use the id when a disabled one shares the name. |
| `ResourceGroup` | yes | The resource group of the web app. |
| `WebApp` | yes | The web app's name. |
| `HealthUrl` | yes | The app's address, https, with no path. The module asks `<HealthUrl>/health` and `<HealthUrl>/health/ready`. |
| `Project` | yes | The project to publish, relative to the repository. |
| `LegacyBuildTimeProperty` | no | An MSBuild property name. When set, the build time is also passed under this name, so a rollback to an older build that reads it still gets its time. |
| `Migrations` | no | Present only for an app with a database to migrate. See below. |
| `RequireCaseInsensitiveCollation` | no | `true` or `false` (default `false`). `true` refuses the migrations unless the database's collation names `_CI_`, a case-insensitive collation. It needs `Migrations` with `SqlServer`, or the config is refused. The collation is read with one `az sql db show --query collation` call, after the database check and before the temporary firewall rule. A collation that cannot be read is refused too. |

The app name (the key, `api` above) must be lower-case letters, digits and hyphens, starting with a letter. It names the deploy tags.

### Migrations keys

| Key | Required | Meaning |
|---|---|---|
| `DbContext` | yes | The EF Core context class name, for `dotnet ef`. |
| `ConnectionSetting` | no | The app setting that holds the connection string. Default `ConnectionStrings__DefaultConnection`. |
| `SqlServer` | no | The logical name of the Azure SQL server. Given, the module checks that the database exists, opens a temporary firewall rule for this machine, and checks that the connection string names this server. Not given, no firewall rule is opened. |
| `Guard` | no | The migration-safety guard: `Project` (its test project) and `Filter` (the `--filter-class` value). See "The migration guard". Without it the guard is unavailable. |
| `Environment` | no | Environment variables for the EF tools and the bundle. Their values must not be secrets. |
| `ThrowawayVariables` | no | Variable names that receive a random value for each run only, such as a signing key that the app needs at startup. Nothing is stored. |
| `ThrowawayDirectories` | no | A map of variable name to marker file name, such as `Storage__DataRoot = '.getthere-data-root'`. For each run the module makes a fresh temporary folder that holds the marker file, sets the variable to its path for the EF commands and the bundle only, and deletes the folder when the run ends, whatever happens. Use it for an app that refuses to start without a data folder. |

The connection string itself is read from the web app and passed to the child process in an environment variable. It is never printed.

### Options

| Option | Default | Meaning |
|---|---|---|
| `ForbiddenSettings` | `WEBSITE_WARMUP_PATH`, `WEBSITE_WARMUP_STATUSES` | App settings that refuse the deploy when present, even when empty. See "The forbidden settings". |
| `SecretPatterns` | bearer tokens, `AccountKey=`, `SharedAccessSignature=`, `password=`, `pwd=` | Regular expressions whose matches are redacted from all output. Given, the list replaces the defaults. Write a lookbehind to keep the key name visible. |
| `TagPushAttempts` | 3 | Tries to push a deploy tag. A refused push reads the next free number again. |
| `TagPushDelaySeconds` | 5 | Pause between tag push attempts. |
| `TagDateFormat` | `yyyy-MM-dd` | The date in a tag name: `yyyy-MM-dd` (the default) or `yyyyMMdd`, for an owner whose tags already use it. A tag reads `deploy/api/2026-11-03-1`. |
| `MinimumSdkMajor` | 8 | The lowest .NET SDK major version the migrations accept. The preflight reads `dotnet --version` and refuses a lower one before anything runs. Set it to the version the app builds with. |
| `RuntimeIdentifier` | `linux-x64` | The `dotnet publish` runtime identifier. |
| `ListRetries` | 6 | Tries again after a refused database connection while reading the migration list. |
| `ListRetryDelaySeconds` | 20 | Pause between those tries. |

## Public functions

### Read-BallastConfig

`Read-BallastConfig -Path <file>`

Reads and validates an `apps.psd1`. Returns the normalised config. Throws with the reason for a missing key, an unknown key or option, a malformed value, or a `Project` or `Guard.Project` outside the repository.

### Invoke-BallastDeploy

`Invoke-BallastDeploy -Config <config> -App <name[]> [-Ref <ref>] [-RepoPath <path>] [-Yes] [-WhatIf] [-RefusePendingMigrations] [-TimeoutSeconds <n>] [-PollSeconds <n>]`

| Parameter | Default | Meaning |
|---|---|---|
| `-Config` | | The config from `Read-BallastConfig`. |
| `-App` | | One or more app names from the config, deployed in the order given. Each app once. An empty list is refused when the parameter is bound. |
| `-Ref` | `HEAD` | A commit, a branch, or a deploy tag. A deploy tag names one app and may only deploy that app. |
| `-RepoPath` | `.` | The repository. |
| `-Yes` | off | Skips the deploy prompt and the migration prompts. |
| `-WhatIf` | off | Runs the guards and the build, and prints the plan. Nothing is migrated, deployed or tagged. |
| `-RefusePendingMigrations` | off | Before an app's migrations, stops that app if any are pending. Nothing is deployed for it. |
| `-TimeoutSeconds` | 900 | How long to wait, after the zip deploy, for the new copy to be proven live. |
| `-PollSeconds` | 5 | Pause between the polls of the switch. |

Returns an object with `ExitCode`, `Message`, and `Apps` (each with `App`, `WebApp` and `Tag`). Use `exit $result.ExitCode`.

What a run does:

- **Guards (nothing changes in Azure).** Fetches `origin` and resolves `-Ref`. Refuses a commit that is not on `origin`, and a missing git committer identity. For an app with migrations, it runs the .NET SDK and dotnet-ef preflight (see `Invoke-BallastMigrations`). For each app, selects its subscription, checks that it runs from its package and that it has no forbidden setting, and reads whether the live commit is newer than the commit being deployed (a warning when the deploy would roll back). Then asks `Deploy ... ?` with a default of no.
- **Build.** Builds each app with `dotnet publish` from a clean worktree of the commit, with `BallastBuildTimeUtc` set to one build time for the run. Zips each app. For an app with migrations, builds the EF bundle from the same worktree (not under `-WhatIf`), with the run's environment: the connection string and the throwaway values, so a startup that needs them builds too.
- **Per app, in order.** Migrations (when the app has them), the zip deploy, the proof of the switch, and the tag. A failure stops the run before the next app. The temporary worktree is removed and the az default subscription is put back in every case.

Exit codes (frozen):

| Code | Meaning |
|---|---|
| 0 | Deployed. Also: `-WhatIf` finished, or the deploy prompt was answered no (nothing changed). |
| 1 | A failure, a guard refused the deploy, or a value could not be read. Nothing was deployed for the app that failed. |
| 2 | The migrations were declined at their prompt, or `-RefusePendingMigrations` found some pending. Nothing was deployed for that app. |
| 3 | Deployed, but the new copy was not proven live within `-TimeoutSeconds`. No tag. |
| 4 | Deployed and healthy, but the tag could not be made or pushed. The push command is printed. |

### Invoke-BallastMigrations

`Invoke-BallastMigrations -Config <config> -App <name> [-RepoPath <path>] [-BundlePath <file>] [-Ref <ref>] [-Database <name>] [-WhatIf] [-Yes]`

Applies one app's pending migrations. Steps, in order: the .NET SDK and dotnet-ef preflight, which runs `dotnet tool restore`; with no `-BundlePath`, a clean worktree of `-Ref`; `dotnet restore` of the app's project (dotnet-ef cannot read a project that was never restored); the connection string (with `-Database`, only its catalog changes); the database check, the collation check (when `RequireCaseInsensitiveCollation` is true), and the temporary firewall rule (when `SqlServer` is set); the pending list from `dotnet ef migrations list --json`; the guard; a summary; the prompt; the bundle (built after the prompt from the worktree when `-BundlePath` is not given); a post-check that nothing is pending. The `ThrowawayDirectories` folders exist from the first EF command until the run ends. Every step after the list is skipped when nothing is pending.

| Parameter | Meaning |
|---|---|
| `-BundlePath` | The EF bundle to run. Build it with `dotnet ef migrations bundle`. Left out, the bundle is built after the prompt from a clean worktree of `-Ref`, with the same guard and prompt. Not needed with `-WhatIf` or when nothing is pending. |
| `-Ref` | `HEAD` | The commit whose migrations are applied when `-BundlePath` is not given. Its clean worktree is the code that the list, the guard and the bundle read. The commit must be in the local clone; it need not be on `origin`. |
| `-Database` | | A rehearsal: the database to use on the same server instead of the catalog in the connection string. Only the catalog changes. Refused when it names the app's own database (case-insensitive), or when it is not a database name. |
| `-WhatIf` | Stops after the summary. Opens and closes the firewall rule, because reading the list has to connect. |
| `-Yes` | Skips the y/N prompt only. The guard checks still run. |

The prompt defaults to no: an empty answer, or anything other than `y` or `yes`, declines. With input redirected, or with `CI` or `GITHUB_ACTIONS` set, the prompt throws (exit 1) and nothing is applied. Pass `-Yes` for unattended runs.

Returns an object with `ExitCode`, `Message`, `Result` (`applied`, `nothing-pending`, `whatif`, `declined` or `failed`), `Pending` and `App`. It also prints the last line:

```text
APPLY-MIGRATIONS result=<applied|nothing-pending|whatif|declined|failed> app=<app> pending=<n>
```

Exit codes (frozen):

| Code | Meaning |
|---|---|
| 0 | Applied, nothing pending, or a finished `-WhatIf`. |
| 1 | Any failure. |
| 2 | Declined at the prompt. |

Each migration runs in its own transaction. When one fails, the ones before it stay applied. They are additive, so the code now serving keeps working, and running the function again resumes.

### Wait-BallastAppSwitched

`Wait-BallastAppSwitched -HealthUrl <url> -Commit <sha> [-TimeoutSeconds <n>] [-PollSeconds <n>] [-Stopwatch <stopwatch>]`

Polls `<HealthUrl>/health`, which must report the commit (a prefix of seven or more characters matches), and `<HealthUrl>/health/ready`, which must answer 200. The switch is proven on two matching polls in a row. A `/health` that reports no commit can never prove the switch. A copy that reports no commit returns 3; see "The legacy no-commit fallback" for why the predecessor's older fallback is not ported.

Returns `ExitCode` (0 proven, 3 not proven within the timeout), `Message`, `Switched`, `SwitchSeconds` and `Proof`.

### Hide-BallastSecret

`Hide-BallastSecret -Text <string> [-Patterns <string[]>]`

Returns the text with every match of the patterns replaced by `<redacted>`. Values registered during a run are replaced first, by value. `-Patterns` uses another list for this call only.

## Build time

The stamp is `BallastBuildTimeUtc`, in exactly `yyyy-MM-ddTHH:mm:ssZ`. The build refuses any other form (a space, a fraction, an offset, an impossible date) before `dotnet` runs.

## The forbidden settings

The deploy refuses an app that has `WEBSITE_WARMUP_PATH` or `WEBSITE_WARMUP_STATUSES` set, even when the value is empty. The refusal names the setting and the command that removes it. The module never suggests setting either one.

The reason: a past deploy of an app with a warm-up setting in place stopped the whole app for 14 to 24 minutes, and the cause was never found. The module decides that the new copy is live from its own proof (`/health` and `/health/ready`), not from a warm-up setting.

The list is the `ForbiddenSettings` option. Replacing it replaces the rule too.

## Run-from-package

The deploy requires `WEBSITE_RUN_FROM_PACKAGE` to be exactly `1`. A URL or `true` there means the app does not run from its package, and the deploy would write over files the running copy still reads. It also refuses `SCM_DO_BUILD_DURING_DEPLOYMENT=true` and `ENABLE_ORYX_BUILD=true`, because the server would build the zip again and the copy that runs would not be the one this module built and tagged. Values are never printed; the output names the settings and pass or fail only.

## The migration guard

The guard is a test run in the app's own repository. The module runs:

```text
dotnet test --project <Guard.Project> --filter-class <Filter>
```

Its output is read for marker lines. A marker line contains `MIGRATION-GUARD ` (a failure reason) or `MIGRATION-ALLOW-LIST ` (a reviewed exception). The match is exact and case-sensitive, with the trailing space. Lines that only resemble a marker are ignored, such as `MIGRATION-GUARDED` or a lower-case `migration-guard`.

Verdicts:

- **clean.** The run exited 0. Migrations go ahead.
- **failed.** The run exited non-zero. The marker lines are printed, and nothing is applied. Exit 1.
- **unavailable.** No guard is configured, its project is not in this commit, or the filter matched no tests. The owner must answer yes at the prompt. `-Yes` refuses an unavailable guard (exit 1).

The guard's own tests decide what a marker means. The module only reads the markers and the exit code.

## Failure paths

| What happens | Result |
|---|---|
| The config is invalid | Throws before any runs. Exit 1 when unhandled. |
| `az` is not signed in, or the subscription is missing, disabled, or named twice | Exit 1, before anything changes. |
| The commit is not on `origin` | Exit 1. Nothing is built. |
| A deploy tag for another app is given | Exit 1. |
| An app does not run from its package, or has a forbidden setting | Exit 1, before any build. |
| `dotnet publish`, the EF bundle, or the zip fails | Exit 1. Nothing has changed in Azure. |
| The database does not exist, or the connection string names another server | Exit 1, before the firewall rule. |
| The database's collation is case-sensitive while `RequireCaseInsensitiveCollation` is true, or cannot be read | Exit 1, before the firewall rule. |
| The .NET SDK is below `MinimumSdkMajor`, or dotnet-ef is not the version `.config/dotnet-tools.json` pins | Exit 1, before any Azure call. |
| `-Database` names the app's own database (in any case), or is not a database name | Exit 1, before the firewall rule. |
| `dotnet restore` of the app's project fails before the migrations are listed | Exit 1, with the end of the restore output, before the connection string is read and before the firewall rule. |
| A run fails after its `ThrowawayDirectories` folder is made | The folder is deleted in the same run. A folder that cannot be deleted is a warning with its path. |
| The migration list cannot be read | Refused connections are retried (`ListRetries`), then exit 1. Other errors exit 1 at once. |
| The guard fails, or is unavailable under `-Yes` | Exit 1. Nothing is applied. |
| All migrations are pending under `-Yes` | Exit 1. Nothing is applied. This looks like an empty or wrong database. |
| The bundle fails, or the post-check finds pending migrations | Exit 1. Earlier migrations stay applied; run again to resume. |
| The migrations are declined | Exit 2. |
| The zip deploy fails | Exit 1, with az's error output. The app is not switched and not tagged. |
| The new copy is not proven live in time | Exit 3, no tag. |
| The tag cannot be made, or every push attempt is refused | Exit 4, with the push command printed. The app is deployed and healthy. |
| The firewall rule cannot be removed | A warning with the command to remove it. The exit code does not change. |
| The run was killed after opening the firewall rule | The rule stays. The next run removes rules named `ballast-deploy-` that are older than two hours. |
| The az default subscription cannot be restored | A warning with the command to set it. |
| The temporary build folder cannot be deleted | A warning. Delete it by hand. |

az writes its progress to stderr, which the module captures. The output appears only when a call fails, so a long zip deploy prints nothing until it ends.

## Exit codes against the predecessor

The exit codes are frozen. Each one below is checked against the predecessor's own scripts as they read on 2026-11-03: `scripts/deploy/deploy.ps1` and `scripts/deploy/apply-migrations.ps1`. The predecessor is the app this module was ported from. Its name is kept out of this library's docs.

`Invoke-BallastDeploy`:

| Code | Ballast.Deploy | Predecessor `deploy.ps1` |
|---|---|---|
| 0 | Deployed; `-WhatIf` finished; the deploy prompt answered no. | Line 196 (answered no). `-WhatIf` prints its plan and falls through to the final `$exitCode = 0`. Deployed: the same final line. |
| 1 | Any failure: a guard refused, a value could not be read, no committer identity, not set up to run from its package, a refused setting, the pending migrations could not be read, the migrations failed. Also the default for an unhandled error. | Lines 145 (identity), 156 (not run from its package), 210 (see below), 264 (migrations failed), and the default `$exitCode = 1` at line 125. |
| 2 | The migrations were declined, or `-RefusePendingMigrations` found some pending. | Line 261 (migrations declined). `-RefusePendingMigrations` has no predecessor equivalent. |
| 3 | Deployed, not proven live within `-TimeoutSeconds`. No tag. | Line 271. |
| 4 | Deployed and healthy, but the tag could not be made or pushed. | Lines 294 (no tag made) and 299 (push failed). |

`Invoke-BallastMigrations`, against `apply-migrations.ps1` (`Set-Outcome`):

| Code | Ballast.Deploy | Predecessor `apply-migrations.ps1` |
|---|---|---|
| 0 | Applied; nothing pending; `-WhatIf` finished. | `applied` (line 656), `nothing-pending` (575), `whatif` (599). |
| 1 | Any failure. | `failed` (664). The default is set at line 113. |
| 2 | Declined at the prompt. | `declined` (613). |

The differences, each deliberate:

- **Code 2 also means `-RefusePendingMigrations` found pending migrations.** The predecessor's `deploy.ps1` has no such option. It never stops an app for pending migrations.
- **A warm-up setting that is present is refused (1).** The predecessor's `_common.ps1` (around line 619) only warns when `WEBSITE_WARMUP_PATH` or `WEBSITE_WARMUP_STATUSES` is not set, and notes when it is set. It refuses neither. The module refuses either one present, following the owner's rule (see "The forbidden settings").
- **The collation check (1) runs only when `RequireCaseInsensitiveCollation` is true.** The predecessor's `apply-migrations.ps1` (lines 506 to 509) runs the same check for its map app only, with no per-app switch.
- **Line 210 has no equivalent.** The predecessor refuses a commit that predates `apply-migrations.ps1` with exit 1. The module has no such check. A commit without the migration project is refused with exit 1 by the build or the migration step.

## The legacy no-commit fallback

The predecessor's `Wait-AppSwitched` (`scripts/deploy/_common.ps1`, around lines 710 to 765) also accepts a copy whose `/health` reports no commit. After three `/health/ready` polls in a row it calls the switch proven, with the proof `ready only, no commit reported`. It was written for copies from before plan R (commit `9f9b6454`, 2026-10-21), which have no commit key.

Ballast.Deploy does not port it. For such a copy, `Wait-BallastAppSwitched` returns 3: the switch is not proven and no tag is made.

- **It cannot tell the copies apart.** The predecessor's own comment in that function says the old copy answers `/health/ready` exactly as the new one does. Ready-only polls would prove a switch that has not happened, and tag a deploy that is not live.
- **No deploy of the predecessor needs it.** All 28 `deploy/*` tags in the predecessor's repository contain `9f9b6454` (checked 2026-11-03 with `git merge-base --is-ancestor`), so a rollback by tag never reaches a copy without the commit key.
- **The proof needs the commit.** An app set up for this module reports its commit in `/health` from its first deploy, so the fallback would only ever apply to a copy that predates it.

If an app ever needs a rollback to a copy with no commit key, the right change is an explicit option for that app with its own proof. It is not a silent default.

## Tests

From the repository root:

```powershell
pwsh -NoProfile -File deploy/Ballast.Deploy/tests/Test-BallastDeploy.ps1
```

The script prints PASS or FAIL for each check and exits 1 when any check fails. It needs no network, no Azure account, no `az`, no `git` remote and no `dotnet` build. Every runner is replaced by a fake that records its calls. The checks cover the config, the collation check, the tag date default and the manifest, secret redaction, the forbidden settings, run-from-package, the zip entry names, the build stamp, the legacy build property, the worktree, the commit and ref checks, the tag numbering and retries, the health proof and its timeout, the migration listing, the guard markers, the prompt default, the firewall rule, the subscription restore, and every exit code of the deploy and the migrations.

The tests do not cover: the real `az`, the real `git` remote or any `git push`, a real `dotnet build` or EF migration, a real Azure SQL firewall, a real zip deploy, the real health endpoints, Windows PowerShell 5.1, timing over long runs, or two deploys running at the same time.
