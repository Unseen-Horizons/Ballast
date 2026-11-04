// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Storage;

namespace Ballast.Storage.Tests;

/// <summary>
/// The sweep of temporary files that a crash left behind. It removes only files named the way this library names them, only
/// when they are old, only in the folder it is given, and it reports what it could not remove instead of throwing.
/// </summary>
public class AtomicFileSweepTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ballast-sweep-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static string TempName() => Guid.NewGuid().ToString("N") + ".atomic-tmp";

    /// <summary>Writes a file in the folder and sets its last write to the given age.</summary>
    private string Write(string name, TimeSpan age)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "leftover");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    [Fact]
    public void Removes_old_temporary_files_and_keeps_fresh_ones()
    {
        var oldName = TempName();
        var old = Write(oldName, TimeSpan.FromHours(2));
        var fresh = Write(TempName(), TimeSpan.FromMinutes(1));

        var result = AtomicFile.SweepStale(_folder, TimeSpan.FromHours(1));

        Assert.Equal(oldName, Path.GetFileName(Assert.Single(result.Removed)));
        Assert.Empty(result.Failed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void Never_touches_a_file_that_only_looks_like_one_of_its_own()
    {
        var names = new[]
        {
            "gtfs.zip.tmp",
            "archive.part",
            "readme.txt",
            new string('a', 31) + ".atomic-tmp",
            new string('A', 32) + ".atomic-tmp",
            Guid.NewGuid().ToString("N") + ".atomic-tmp.bak",
            Guid.NewGuid().ToString("N") + ".atomictmp",
            Guid.NewGuid().ToString("N") + "x.atomic-tmp"
        };
        var paths = names.Select(name => Write(name, TimeSpan.FromDays(3))).ToArray();

        // A folder with a temp-looking name, and an old temp file inside a subfolder: neither is a file in this folder.
        var lookalikeFolder = Path.Combine(_folder, TempName());
        Directory.CreateDirectory(lookalikeFolder);
        var nested = Path.Combine(_folder, "nested");
        Directory.CreateDirectory(nested);
        var nestedTemp = Path.Combine(nested, TempName());
        File.WriteAllText(nestedTemp, "x");
        File.SetLastWriteTimeUtc(nestedTemp, DateTime.UtcNow - TimeSpan.FromDays(3));

        var result = AtomicFile.SweepStale(_folder, TimeSpan.FromHours(1));

        Assert.Empty(result.Removed);
        Assert.Empty(result.Failed);
        Assert.All(paths, path => Assert.True(File.Exists(path), path));
        Assert.True(File.Exists(nestedTemp));
        Assert.True(Directory.Exists(lookalikeFolder));
    }

    [Fact]
    public void A_missing_folder_has_nothing_to_sweep_and_is_not_created()
    {
        var missing = Path.Combine(_folder, "missing");

        var result = AtomicFile.SweepStale(missing, TimeSpan.Zero);

        Assert.Empty(result.Removed);
        Assert.Empty(result.Failed);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void A_negative_age_is_refused()
    {
        Directory.CreateDirectory(_folder);

        Assert.Throws<ArgumentOutOfRangeException>(() => AtomicFile.SweepStale(_folder, TimeSpan.FromSeconds(-1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_folder_is_refused(string? folder)
    {
        Assert.ThrowsAny<ArgumentException>(() => AtomicFile.SweepStale(folder!, TimeSpan.Zero));
    }

    [Fact]
    public void A_temporary_file_that_cannot_be_removed_is_reported_and_not_thrown()
    {
        // Only Windows refuses to delete a file that another handle holds open without sharing delete. Elsewhere the
        // removal succeeds, so there is no failure to report.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that another handle holds open.");
        var name = TempName();
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(_folder);
        using var held = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        held.WriteByte(1);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(2));

        var result = AtomicFile.SweepStale(_folder, TimeSpan.FromHours(1));

        Assert.Empty(result.Removed);
        var failure = Assert.Single(result.Failed);
        Assert.Equal(path, failure.FullPath);
        Assert.NotNull(failure.Error);
        Assert.True(File.Exists(path));
    }
}
