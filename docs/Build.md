# Ballast.Build usage guide

Ballast.Build is one development-dependency package with no assembly. A project that references it gets shared build settings:
nullable and analyzer settings, analyzer severities, NuGet vulnerability checks, and the build-time stamp that `/health` reports.
It adds no code to the app and no runtime dependency, and it references no package, so it pins no analyzer version.

This guide covers every setting, how a project overrides one, and every way the package fails. The short example that runs as
written is in `src/Ballast.Build/README.md`.

## Install

```
dotnet add package Ballast.Build --prerelease
```

The package is marked `DevelopmentDependency`, so NuGet keeps it out of the packages that reference your project. Nothing
ships with the app except the settings it applied at build time.

## What the package sets

The package imports `build/Ballast.Build.props` before the project body, and `build/Ballast.Build.targets` after it.

| Setting | Value | What it does |
|---|---|---|
| `Nullable` | `enable` | Nullable reference types on. |
| `ImplicitUsings` | `enable` | The default global usings. |
| `LangVersion` | `latest` | The newest C# the SDK supports. |
| `EnableNETAnalyzers` | `true` | The .NET analyzers run at build time. |
| `AnalysisLevel` | `latest-recommended` | The recommended analyzer rules are on as warnings, not only the default set. |
| `EnforceCodeStyleInBuild` | `true` | Code-style rules from `.editorconfig` run in the build, not only in the IDE. |
| `Deterministic` | `true` | The same source gives the same binary. |
| `NuGetAudit` | `true` | Every package the project uses is checked for a known vulnerability at restore. |
| `NuGetAuditMode` | `all` | The check covers direct and transitive packages. |
| `NuGetAuditLevel` | `low` | Advisories of every severity are reported. |
| `WarningsAsErrors` | adds `NU1902;NU1903;NU1904` | A moderate, high or critical advisory fails the build. |
| `GlobalAnalyzerConfigFiles` | `build/Ballast.Build.globalconfig` | The analyzer severities below. |

### Overriding a setting

The props are plain assignments, and they run before the project body. A value the project sets after the import wins, so a
project that needs `Nullable` off writes `<Nullable>disable</Nullable>` in its own `PropertyGroup`. This follows from MSBuild's
evaluation order; the package does not test each override separately.

`WarningsAsErrors` is the exception to watch. The package appends to it, but a project that sets `WarningsAsErrors` replaces the
list, and the NuGet audit codes are then lost. Keep them in the project's own value:

```xml
<WarningsAsErrors>$(WarningsAsErrors);NU1902;NU1903;NU1904</WarningsAsErrors>
```

## Analyzer severities

`build/Ballast.Build.globalconfig` is read for every file of the project. It has no section headers, and each key applies to the
whole project.

| Rule | Severity | Why |
|---|---|---|
| CA1305 | warning | Culture-sensitive formatting and parsing. |
| CA2016 | warning | A method that takes a `CancellationToken` must pass it on. |
| CA2201 | warning | A reserved exception type (`Exception`, `SystemException`, `ApplicationException`) is thrown. |
| CA2017 | error | A logging template's placeholder count does not match its arguments. A duplicated placeholder throws inside a catch block, so this one fails the build even without `-warnaserror`. |
| CA1848 | none | High-performance `LoggerMessage` delegates: a style preference. |
| CA1873 | none | String interpolation in a logging call: a style preference. |
| CA1000 | none | Static members on generic types. |
| CA1051 | none | Visible instance fields. |
| CA1711 | none | Type names that end in a collection suffix. |
| CA1716 | none | Identifiers that clash with a reserved word in some language. |
| CA1725 | none | Parameter names that differ from the base member. |

The rules set to `none` are style preferences whose hits would bury the correctness rules above.

Two things follow from how these settings combine:

- **A project's own `.editorconfig` overrides the global config for the same rule.** A test shows this: a sample whose
  `.editorconfig` sets `dotnet_diagnostic.CA1051.severity = warning` gets the warning back, though the global config says `none`.
  To turn a rule on for one project, set it in that project's `.editorconfig`.
- **Every other recommended rule still runs.** `latest-recommended` turns on the rules the table does not list, and they fail
  the build under `-warnaserror`. A consumer check showed this: a type declared without a namespace gets CA1050.

CI builds with `-warnaserror`, so each warning in the table, and each recommended rule the table leaves on, fails the build there.

## The build stamp

The stamp is the time a copy was built. It is written into the assembly as the metadata key `BuiltAtUtc`, and
`Ballast.Health` reads that key (its build-info default is `BuiltAtUtc`) so `/health` reports the build time of the running copy.

### Passing the time

```
dotnet build -c Release -p:BallastBuildTimeUtc=2026-11-03T10:00:00Z
```

The value must be exactly `yyyy-MM-ddTHH:mm:ssZ`: a UTC time with a `Z` and no fraction or offset. A deploy script passes it;
a local build leaves it out.

### An older property name

An app that stamped its builds under an older property name maps that name to `BallastBuildTimeUtc` in its own project, in the
project body:

```xml
<PropertyGroup>
  <BallastBuildTimeUtc Condition="'$(BallastBuildTimeUtc)' == ''">$(LegacyBuildTimeUtc)</BallastBuildTimeUtc>
</PropertyGroup>
```

The package reads only the Ballast name, so it names no app. Tests cover this pattern: with both names given, the Ballast name wins,
and a legacy name the project does not map is not read.

### How the stamp fails

| Situation | Result |
|---|---|
| `BallastBuildTimeUtc` not given, or empty | No stamp. The copy has no build time, and `/health` reports it as null. Never a guess. |
| A valid stamp | The build writes `BuiltAtUtc`. |
| A value that is not exactly `yyyy-MM-ddTHH:mm:ssZ` (for example `tomorrow`, `2026-11-03T10:00:00+01:00`, or a space instead of `T`) | The build stops before compiling, with an error that names `BallastBuildTimeUtc` and quotes the value. Exit code 1. |
| A legacy value that the project maps and that is invalid | The same error, because the mapped value is the Ballast value. |

## NuGet audit

Restore checks every package against NuGet's vulnerability data:

- A **low** advisory is reported as a warning, `NU1901`.
- A **moderate** advisory is an error, `NU1902`. A **high** one is `NU1903`, and a **critical** one is `NU1904`. Each fails the build.
- If NuGet cannot read the vulnerability data, it reports `NU1900` as a warning (NuGet's behaviour, not tested here). A build with
  `-warnaserror` then fails on that warning. CI sees this when it cannot reach the advisory feed.

An app makes its own exception in its own project, with the reason next to it, using NuGet's `NuGetAuditSuppress` item. The
package does not list exceptions. This path is not tested here.

## How the package fails

| Situation | What happens |
|---|---|
| The stamp is malformed | The build fails with the stamp error above (exit 1). |
| A project sets `WarningsAsErrors` without the NuGet codes | The vulnerability codes stop failing the build. The project's own value replaces the package's list. |
| An analyzer rule is `error` (CA2017) | The build fails without `-warnaserror`. |
| An analyzer rule is `warning` or a recommended rule fires | The build succeeds, unless `-warnaserror` is set, in which case it fails. |
| NuGet cannot read vulnerability data | NU1900 warning; a `-warnaserror` build fails. |

## Packing

`dotnet pack src/Ballast.Build/Ballast.Build.csproj -c Release -o artifacts/packages` writes one package, `Ballast.Build.<version>.nupkg`,
and exits 0. The package holds `build/Ballast.Build.props`, `build/Ballast.Build.targets`, `build/Ballast.Build.globalconfig` and
`README.md`, and nothing else.

The project sets `IncludeSymbols` to `false`. An assembly-less package has no symbols, so its symbols package would be empty,
and NuGet refuses an empty package. Symbols are off for this package only; the other Ballast libraries keep them.

What was checked:

- The package file is written with the contents above.
- A consumer project with the README example restores from a local feed, and builds with and without the stamp, and with an
  invalid one. The results are in the section above.

## Tests

`tests/Ballast.Build.Tests` builds a sample project in a temporary folder, with the package's props and targets imported from
the repository, and reads what the build wrote. It runs 14 cases:

- the stamp is written as `BuiltAtUtc`; a local build writes none;
- a legacy name is read only when the project maps it, and the Ballast name wins;
- four invalid values fail the build with the named error;
- the global config switches off CA1051 for a consumer, and a control sample without the package reports it;
- a project's own `.editorconfig` overrides the global config;
- a clean sample builds with the package's settings on.

The tests need the .NET 10 SDK and no network. Each sample build takes about four to five seconds, and the whole run about a
minute and a quarter.
