// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

// A metadata value under a key other than the default, so that the key option can be tested against a real assembly.
[assembly: AssemblyMetadata("Ballast.Health.TestBuiltAt", "2026-10-21T12:00:00+02:00")]

namespace Ballast.Health.Tests;

/// <summary>What <c>/health</c> reports as the build: a plain commit sha after the <c>+</c>, and a UTC build time. Anything else is absent.</summary>
public class BuildInfoTests
{
    private const string Sha = "0c40e420a1b2c3d4e5f60718293a4b5c6d7e8f90";

    [Fact]
    public void A_40_hex_commit_comes_back_in_lowercase()
    {
        var build = BuildInfoReader.Parse("1.0.0+" + Sha.ToUpperInvariant(), null);

        Assert.Equal(Sha, build.Commit);
    }

    [Fact]
    public void A_version_with_no_plus_has_no_commit()
    {
        Assert.Null(BuildInfoReader.Parse("1.0.0", null).Commit);
    }

    [Theory]
    [InlineData("1.0.0+abc")]
    [InlineData("1.0.0+xyz1234")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+0c40e420-dirty")]
    public void A_suffix_that_is_not_a_plain_sha_has_no_commit(string informational)
    {
        Assert.Null(BuildInfoReader.Parse(informational, null).Commit);
    }

    [Fact]
    public void A_build_time_is_normalised_to_utc_seconds()
    {
        var build = BuildInfoReader.Parse(null, "2026-10-21T12:00:00+02:00");

        Assert.Equal("2026-10-21T10:00:00Z", build.BuiltAtUtc);
    }

    [Theory]
    [InlineData("not a time")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unreadable_build_time_is_null(string? raw)
    {
        Assert.Null(BuildInfoReader.Parse(null, raw).BuiltAtUtc);
    }

    [Fact]
    public void Reading_the_test_assembly_never_throws()
    {
        // A local test run carries neither value, so both are null; either way the result is a well-formed pair.
        var build = BuildInfoReader.Read(typeof(BuildInfoTests).Assembly);

        Assert.True(build.Commit is null || build.Commit.Length >= 7);
    }

    [Fact]
    public void The_build_time_is_read_under_the_key_the_caller_names()
    {
        var build = BuildInfoReader.Read(typeof(BuildInfoTests).Assembly, "Ballast.Health.TestBuiltAt");

        Assert.Equal("2026-10-21T10:00:00Z", build.BuiltAtUtc);
    }

    [Fact]
    public void The_default_key_does_not_find_a_build_time_under_another_name()
    {
        var build = BuildInfoReader.Read(typeof(BuildInfoTests).Assembly);

        Assert.Null(build.BuiltAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_key_is_refused(string key)
    {
        Assert.Throws<ArgumentException>(() => BuildInfoReader.Read(typeof(BuildInfoTests).Assembly, key));
    }

    [Fact]
    public void A_missing_assembly_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => BuildInfoReader.Read(null!));
    }
}
