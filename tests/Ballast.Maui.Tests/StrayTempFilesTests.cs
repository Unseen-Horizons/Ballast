// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>The one-time sweep of the fixed-name temporary files: only old matches go, a young file waits, and the marker is set only when the sweep was complete.</summary>
public sealed class StrayTempFilesTests
{
    [Fact]
    public void Old_matching_files_go_and_young_and_other_files_stay()
    {
        using var root = new TempRoot();
        var nested = Path.Combine(root.Path, "a", "b");
        Directory.CreateDirectory(nested);
        var old = Path.Combine(nested, "imported.bin.tmp");
        var young = Path.Combine(root.Path, "fresh.tmp");
        var other = Path.Combine(root.Path, "keep.bin");
        var atomic = Path.Combine(root.Path, "current.atomic-tmp");
        File.WriteAllText(old, "x");
        File.WriteAllText(young, "x");
        File.WriteAllText(other, "x");
        File.WriteAllText(atomic, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(atomic, DateTime.UtcNow.AddHours(-1));
        var log = new RecordingLog();

        var result = StrayTempFiles.Sweep([root.Path], ".tmp", StrayTempFiles.DefaultMinimumAge, log);

        Assert.Equal("imported.bin.tmp", Path.GetFileName(Assert.Single(result.Removed)));
        Assert.Equal(1, result.TooYoung);
        Assert.Empty(result.Failed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(young));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(atomic));
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void A_folder_that_does_not_exist_has_nothing_to_sweep()
    {
        using var root = new TempRoot();

        var result = StrayTempFiles.Sweep([Path.Combine(root.Path, "missing")], ".tmp", StrayTempFiles.DefaultMinimumAge, new RecordingLog());

        Assert.Empty(result.Removed);
        Assert.Equal(0, result.TooYoung);
        Assert.Empty(result.Failed);
    }

    [Fact]
    public void The_suffix_must_start_with_a_dot_and_the_age_must_not_be_negative()
    {
        var log = new RecordingLog();

        Assert.Throws<ArgumentException>(() => StrayTempFiles.Sweep([], "tmp", StrayTempFiles.DefaultMinimumAge, log));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrayTempFiles.Sweep([], ".tmp", TimeSpan.FromMinutes(-1), log));
    }

    [Fact]
    public void RunOnce_sweeps_once_and_then_stays_quiet()
    {
        using var root = new TempRoot();
        var stale = Path.Combine(root.Path, "stale.tmp");
        File.WriteAllText(stale, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));
        var preferences = new FakePreferenceStore();
        var log = new RecordingLog();

        var first = StrayTempFiles.RunOnce([root.Path], ".tmp", preferences, "stray.done", null, log, StrayTempFiles.DefaultMinimumAge);
        var second = StrayTempFiles.RunOnce([root.Path], ".tmp", preferences, "stray.done", null, log, StrayTempFiles.DefaultMinimumAge);

        Assert.NotNull(first);
        Assert.Single(first!.Removed);
        Assert.Null(second);
        Assert.True(preferences.Get("stray.done", false, null));
    }

    [Fact]
    public void A_young_file_leaves_the_marker_unset_so_the_sweep_runs_again()
    {
        using var root = new TempRoot();
        File.WriteAllText(Path.Combine(root.Path, "fresh.tmp"), "x");
        var preferences = new FakePreferenceStore();
        var log = new RecordingLog();

        var first = StrayTempFiles.RunOnce([root.Path], ".tmp", preferences, "stray.done", null, log, StrayTempFiles.DefaultMinimumAge);
        var second = StrayTempFiles.RunOnce([root.Path], ".tmp", preferences, "stray.done", null, log, StrayTempFiles.DefaultMinimumAge);

        Assert.Equal(1, first!.TooYoung);
        Assert.NotNull(second);
        Assert.False(preferences.Get("stray.done", false, null));
    }

    [Fact]
    public void A_blank_marker_key_is_refused()
    {
        Assert.Throws<ArgumentException>(() => StrayTempFiles.RunOnce([], ".tmp", new FakePreferenceStore(), " ", null, new RecordingLog(), StrayTempFiles.DefaultMinimumAge));
    }
}
