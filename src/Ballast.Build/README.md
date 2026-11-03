# Ballast.Build

Shared build settings for .NET projects, from one development-dependency package with no assembly: nullable and analyzer
settings, analyzer severities, NuGet audit, and the build-time stamp that `/health` reports.

Add it to a project. It is used by the build only and never ships with the app:

```
dotnet add package Ballast.Build --prerelease
```

## Example

`MyApp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Ballast.Build" Version="0.1.0-preview.1" />
  </ItemGroup>

</Project>
```

Build it for release with the time the copy was built. `/health` then reports that time:

```
dotnet build -c Release -p:BallastBuildTimeUtc=2026-11-03T10:00:00Z
```

The package turns on nullable reference types, implicit usings and the .NET analyzers, sets the analyzer severities in its
global config, and makes a moderate, high or critical NuGet vulnerability a build error. A build without
`BallastBuildTimeUtc` gets no build time, and `/health` reports it as null, never a guess.

The usage guide, with every setting, every way it fails and how an app overrides one, is `docs/Build.md` in the repository.
