// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>Per-owner sealed files: owners are kept apart, the file holds no readable text, a write that fails answers false, and a read that fails answers null and is logged.</summary>
public sealed class OwnedSealedStoreTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly FakeSecureValues _values = new();
    private readonly RecordingLog _log = new();
    private readonly SealedKeyProvider _keys;

    public OwnedSealedStoreTests()
    {
        _keys = new SealedKeyProvider(_values, new SecureValueReader(_values, _log), "test.cacheKey");
    }

    public void Dispose()
    {
        _keys.Dispose();
        _root.Dispose();
    }

    private OwnedSealedStore Create(string folder = "owned") =>
        new(new OwnedStoreOptions { FolderName = folder }, new FakeAppPaths(_root.Path), _keys, _log);

    [Fact]
    public async Task A_saved_record_reads_back_equal()
    {
        using var store = Create();

        Assert.True(await store.SaveAsync("user-1", "profile", new Sample("Ana", 2), TestJsonContext.Default.Sample));

        Assert.Equal(new Sample("Ana", 2), await store.ReadAsync("user-1", "profile", TestJsonContext.Default.Sample));
        Assert.Empty(_log.Failures);
    }

    [Fact]
    public async Task The_file_on_disk_holds_no_readable_text()
    {
        using var store = Create();
        await store.SaveAsync("user-1", "profile", new Sample("Ana", 2), TestJsonContext.Default.Sample);

        var bytes = File.ReadAllBytes(store.PathFor("user-1", "profile"));

        Assert.DoesNotContain("Ana", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_owner_sees_only_its_own_files()
    {
        using var store = Create();
        await store.SaveAsync("user-1", "profile", new Sample("Ana", 2), TestJsonContext.Default.Sample);

        Assert.Null(await store.ReadAsync("user-2", "profile", TestJsonContext.Default.Sample));
        Assert.NotEqual(store.DirectoryFor("user-1"), store.DirectoryFor("user-2"));
    }

    [Fact]
    public void The_owner_is_hashed_into_a_32_character_folder_name()
    {
        using var store = Create();
        var folder = Path.GetFileName(store.DirectoryFor("user-1"));

        Assert.Equal(32, folder.Length);
        Assert.All(folder, c => Assert.True(Uri.IsHexDigit(c)));
        Assert.DoesNotContain("user-1", store.DirectoryFor("user-1"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_folder_sits_under_the_app_data_folder_and_the_file_takes_the_extension()
    {
        using var store = Create("owned");

        Assert.StartsWith(Path.Combine(_root.Path, "data", "owned"), store.RootFolder, StringComparison.Ordinal);
        Assert.EndsWith(".bin", store.PathFor("user-1", "profile"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_file_reads_as_null_without_a_log_line()
    {
        using var store = Create();

        Assert.Null(await store.ReadAsync("user-1", "profile", TestJsonContext.Default.Sample));
        Assert.Empty(_log.Failures);
    }

    [Fact]
    public async Task A_tampered_file_reads_as_null_is_logged_and_is_left_in_place()
    {
        using var store = Create();
        var path = store.PathFor("user-1", "profile");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[64]);

        Assert.Null(await store.ReadAsync("user-1", "profile", TestJsonContext.Default.Sample));
        Assert.Single(_log.Failures);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task A_write_that_cannot_happen_answers_false_and_is_logged()
    {
        Directory.CreateDirectory(Path.Combine(_root.Path, "data"));
        File.WriteAllText(Path.Combine(_root.Path, "data", "owned"), "in the way");
        using var store = Create("owned");

        Assert.False(await store.SaveAsync("user-1", "profile", new Sample("Ana", 2), TestJsonContext.Default.Sample));
        Assert.Single(_log.Failures);
    }

    [Fact]
    public async Task Clear_removes_one_owner_and_leaves_the_others()
    {
        using var store = Create();
        await store.SaveAsync("user-1", "profile", new Sample("Ana", 2), TestJsonContext.Default.Sample);
        await store.SaveAsync("user-2", "profile", new Sample("Ben", 3), TestJsonContext.Default.Sample);

        store.Clear("user-1");

        Assert.Null(await store.ReadAsync("user-1", "profile", TestJsonContext.Default.Sample));
        Assert.Equal(new Sample("Ben", 3), await store.ReadAsync("user-2", "profile", TestJsonContext.Default.Sample));
        Assert.True(store.HasAnyData());
    }

    [Fact]
    public async Task HasAnyData_is_false_until_something_is_saved_and_false_again_once_cleared()
    {
        using var store = Create();
        Assert.False(store.HasAnyData());

        await store.SaveAsync("user-1", "profile", new Sample("Ana", 2), TestJsonContext.Default.Sample);
        Assert.True(store.HasAnyData());

        store.Clear("user-1");
        Assert.False(store.HasAnyData());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void A_folder_name_must_be_one_name(string folder)
    {
        Assert.ThrowsAny<ArgumentException>(() => Create(folder));
    }

    [Fact]
    public void The_extension_must_start_with_a_dot()
    {
        Assert.Throws<ArgumentException>(() => new OwnedSealedStore(new OwnedStoreOptions { FolderName = "owned", FileExtension = "txt" }, new FakeAppPaths(_root.Path), _keys, _log));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(65)]
    public void Owner_digits_stay_between_16_and_64(int digits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnedSealedStore(new OwnedStoreOptions { FolderName = "owned", OwnerDigits = digits }, new FakeAppPaths(_root.Path), _keys, _log));
    }

    [Fact]
    public void The_defaults_are_the_bin_extension_and_32_owner_digits()
    {
        var options = new OwnedStoreOptions { FolderName = "owned" };

        Assert.Equal(".bin", options.FileExtension);
        Assert.Equal(32, options.OwnerDigits);
    }

    [Fact]
    public void A_blank_owner_and_a_file_name_with_a_path_are_refused()
    {
        using var store = Create();

        Assert.Throws<ArgumentException>(() => store.DirectoryFor(" "));
        Assert.Throws<ArgumentException>(() => store.PathFor("user-1", "a/b"));
    }
}
