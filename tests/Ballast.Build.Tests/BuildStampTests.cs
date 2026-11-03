// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace Ballast.Build.Tests;

/// <summary>
/// Builds a sample project in a temporary folder and reads what the build wrote. The sample imports the Ballast.Build props and
/// targets from this repository, as a consumer's project imports them from the package. Each test removes its folder at the end.
/// </summary>
public class BuildStampTests
{
    private const string Clean = """
        namespace BuildSample;

        public static class Probe
        {
            public static string Text() => "plain";
        }
        """;

    // A public instance field is CA1051. The default SDK reports it; the package's global config switches that rule off.
    private const string PublicField = """
        namespace BuildSample;

        public sealed class Probe
        {
            public int Count;
        }
        """;

    [Fact]
    public void A_ballast_build_time_is_written_as_the_builtAtUtc_metadata_value()
    {
        using var sample = Sample.Create(Clean);

        var result = sample.Build("-p:BallastBuildTimeUtc=2026-11-03T10:00:00Z");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["2026-11-03T10:00:00Z"], BuiltAtValues(sample.Assembly));
    }

    [Fact]
    public void A_legacy_build_time_is_read_when_the_app_maps_its_old_name()
    {
        using var sample = Sample.Create(Clean, mapLegacyName: true);

        var result = sample.Build("-p:LegacyBuildTimeUtc=2026-10-21T08:30:00Z");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["2026-10-21T08:30:00Z"], BuiltAtValues(sample.Assembly));
    }

    [Fact]
    public void Without_the_app_mapping_a_legacy_value_is_not_read()
    {
        using var sample = Sample.Create(Clean);

        var result = sample.Build("-p:LegacyBuildTimeUtc=2026-10-21T08:30:00Z");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(BuiltAtValues(sample.Assembly));
    }

    [Fact]
    public void A_legacy_value_that_is_not_an_exact_utc_stamp_fails_the_build()
    {
        using var sample = Sample.Create(Clean, mapLegacyName: true);

        var result = sample.Build("-p:LegacyBuildTimeUtc=tomorrow");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("BallastBuildTimeUtc", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void When_both_names_are_given_the_Ballast_name_wins()
    {
        using var sample = Sample.Create(Clean, mapLegacyName: true);

        var result = sample.Build("-p:BallastBuildTimeUtc=2026-11-03T10:00:00Z", "-p:LegacyBuildTimeUtc=2026-10-21T08:30:00Z");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["2026-11-03T10:00:00Z"], BuiltAtValues(sample.Assembly));
    }

    [Fact]
    public void A_local_build_with_no_build_time_writes_no_metadata_value()
    {
        using var sample = Sample.Create(Clean);

        var result = sample.Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(BuiltAtValues(sample.Assembly));
    }

    [Theory]
    [InlineData("2026-11-03T10:00:00+01:00")]
    [InlineData("2026-11-03 10:00:00Z")]
    [InlineData("2026-11-03T10:00Z")]
    [InlineData("tomorrow")]
    public void A_build_time_that_is_not_an_exact_utc_stamp_fails_the_build(string value)
    {
        using var sample = Sample.Create(Clean);

        var result = sample.Build($"-p:BallastBuildTimeUtc={value}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("BallastBuildTimeUtc", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_global_config_switches_off_a_default_rule_for_the_consumer()
    {
        using var sample = Sample.Create(PublicField);

        var result = sample.Build();

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("CA1051", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_package_the_same_sample_reports_the_rule_the_global_config_switches_off()
    {
        using var sample = Sample.Create(PublicField, importPackage: false);

        var result = sample.Build();

        Assert.Contains("CA1051", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_projects_own_editorconfig_overrides_the_global_default_for_a_rule()
    {
        using var sample = Sample.Create(PublicField, editorConfig: "root = true\n\n[*.cs]\ndotnet_diagnostic.CA1051.severity = warning\n");

        var result = sample.Build();

        Assert.Contains("CA1051", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_sample_builds_with_the_package_settings_on()
    {
        using var sample = Sample.Create(Clean);

        var result = sample.Build();

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(sample.Assembly), "The build wrote no assembly.");
    }

    /// <summary>The BuiltAtUtc metadata values of a built assembly, read in a collectible context so the test process loads nothing permanently.</summary>
    private static List<string> BuiltAtValues(string assemblyPath)
    {
        var context = new AssemblyLoadContext("ballast-build-probe-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            // The bytes are read at once and the file is closed, so the sample folder can be deleted after this returns.
            using var stream = new MemoryStream(File.ReadAllBytes(assemblyPath));
            var assembly = context.LoadFromStream(stream);
            return assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(attribute => attribute.Key == "BuiltAtUtc")
                .Select(attribute => attribute.Value ?? string.Empty)
                .ToList();
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>A sample project in a temporary folder.</summary>
    private sealed class Sample : IDisposable
    {
        private readonly string _folder;
        private readonly string _project;

        private Sample(string folder, string project)
        {
            _folder = folder;
            _project = project;
        }

        /// <summary>The built assembly's path, after <see cref="Build"/> has run.</summary>
        public string Assembly => Path.Combine(_folder, "bin", "Release", "net10.0", "BuildSample.dll");

        /// <summary>Makes a sample with one source file, and optionally its own .editorconfig.</summary>
        /// <param name="code">The source of Probe.cs.</param>
        /// <param name="importPackage">False builds the same project without the package, as a control.</param>
        /// <param name="editorConfig">The text of the sample's own .editorconfig, or null for none.</param>
        /// <param name="mapLegacyName">True adds the app-side mapping the docs show: a legacy property named LegacyBuildTimeUtc
        /// becomes BallastBuildTimeUtc when BallastBuildTimeUtc is not given.</param>
        public static Sample Create(string code, bool importPackage = true, string? editorConfig = null, bool mapLegacyName = false)
        {
            var folder = Path.Combine(Path.GetTempPath(), "ballast-build-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            // The package sets the analysis level it needs. The control has no package, so it sets that same level itself: then the
            // only difference between the two samples is the package's global config.
            var analysis = importPackage ? string.Empty : "<AnalysisLevel>latest-recommended</AnalysisLevel>";
            var mapping = mapLegacyName
                ? """<PropertyGroup><BallastBuildTimeUtc Condition="'$(BallastBuildTimeUtc)' == ''">$(LegacyBuildTimeUtc)</BallastBuildTimeUtc></PropertyGroup>"""
                : string.Empty;
            var imports = importPackage
                ? $"""
                  <Import Project="{Path.Combine(RepositoryRoot(), "src", "Ballast.Build", "build", "Ballast.Build.props")}" />
                  {mapping}
                  <Import Project="{Path.Combine(RepositoryRoot(), "src", "Ballast.Build", "build", "Ballast.Build.targets")}" />
                  """
                : string.Empty;
            var project = Path.Combine(folder, "BuildSample.csproj");
            File.WriteAllText(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Library</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>BuildSample</AssemblyName>
                    {analysis}
                  </PropertyGroup>
                  {imports}
                </Project>
                """);
            File.WriteAllText(Path.Combine(folder, "Probe.cs"), code);
            if (editorConfig is not null)
            {
                File.WriteAllText(Path.Combine(folder, ".editorconfig"), editorConfig);
            }

            return new Sample(folder, project);
        }

        /// <summary>Builds the sample in Release with the given -p arguments, and returns the exit code and the output.</summary>
        public BuildResult Build(params string[] properties)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[] { "build", _project, "-c", "Release", "--nologo" }.Concat(properties))
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet could not be started.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new BuildResult(process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
        }

        public void Dispose()
        {
            Directory.Delete(_folder, recursive: true);
        }

        private static string RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Ballast.slnx")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root (Ballast.slnx).");
        }
    }

    /// <summary>The exit code and the console output of one build.</summary>
    private sealed record BuildResult(int ExitCode, string Output);
}
