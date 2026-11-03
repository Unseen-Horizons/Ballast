// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Tests;

/// <summary>
/// The R8 keep rule for Credential Manager ships in the package: the build file imports it into every Android app that references the
/// package, and the project packs the build folder. Without it Google sign-in fails at run time while the build succeeds.
/// </summary>
public class ProguardConsumerRuleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ballast.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Ballast.slnx");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(parts.Prepend(RepoRoot()).ToArray()));

    [Fact]
    public void The_package_imports_the_rule_into_android_apps_only()
    {
        var targets = Read("src", "Ballast.Maui", "build", "Ballast.Maui.targets");

        Assert.Contains("ProguardConfiguration Include=\"$(MSBuildThisFileDirectory)Ballast.Maui.proguard.cfg\"", targets, StringComparison.Ordinal);
        Assert.Contains("GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'", targets, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rule_keeps_the_play_services_credential_provider()
    {
        var rule = Read("src", "Ballast.Maui", "build", "Ballast.Maui.proguard.cfg");

        Assert.Contains("-if class androidx.credentials.CredentialManager", rule, StringComparison.Ordinal);
        Assert.Contains("-keep class androidx.credentials.playservices.**", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void The_package_ships_its_build_folder()
    {
        var project = Read("src", "Ballast.Maui", "Ballast.Maui.csproj");

        Assert.Contains("<None Include=\"build\\**\" Pack=\"true\" PackagePath=\"build\" />", project, StringComparison.Ordinal);
    }
}
