// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Ballast.Storage;

namespace Ballast.Storage.Tests;

/// <summary>
/// The atomic write: the content lands whole or not at all, an existing file is replaced or refused as the mode says, and a
/// failed or cancelled write leaves no temporary file behind. Both the create-new refusal and the replacement are covered, each
/// with a writer that fails, a cancellation and two writers racing for one name. The synchronous overloads get the same cases.
/// </summary>
public class AtomicFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ballast-atomic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string PathNamed(string name) => Path.Combine(_folder, name);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(string path) => File.ReadAllText(path, Encoding.UTF8);

    /// <summary>The temporary files left in a folder. A finished or failed write must leave none.</summary>
    private string[] TempFiles() => Directory.Exists(_folder) ? Directory.GetFiles(_folder, "*.atomic-tmp") : [];

    [Fact]
    public async Task Writes_the_bytes_and_leaves_only_the_file()
    {
        var path = PathNamed("receipt.txt");

        await AtomicFile.WriteAsync(path, Bytes("Receipt 42"), AtomicWriteMode.Overwrite);

        Assert.Equal("Receipt 42", Text(path));
        Assert.Equal("receipt.txt", Path.GetFileName(Assert.Single(Directory.GetFiles(_folder))));
    }

    [Fact]
    public async Task Creates_the_folders_on_demand()
    {
        var path = Path.Combine(_folder, "uploads", "2026", "10", "photo.jpg");

        await AtomicFile.WriteAsync(path, Bytes("x"), AtomicWriteMode.NeverOverwrite);

        Assert.Equal("x", Text(path));
    }

    [Fact]
    public async Task Overwrite_replaces_an_existing_file()
    {
        var path = PathNamed("settings.json");
        await AtomicFile.WriteAsync(path, Bytes("old"), AtomicWriteMode.Overwrite);

        await AtomicFile.WriteAsync(path, Bytes("new"), AtomicWriteMode.Overwrite);

        Assert.Equal("new", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task NeverOverwrite_refuses_an_existing_file_and_leaves_it_untouched()
    {
        var path = PathNamed("receipt.txt");
        await AtomicFile.WriteAsync(path, Bytes("first"), AtomicWriteMode.NeverOverwrite);

        var ex = await Assert.ThrowsAsync<IOException>(() => AtomicFile.WriteAsync(path, Bytes("second"), AtomicWriteMode.NeverOverwrite));

        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
        Assert.Equal("first", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task NeverOverwrite_creates_a_missing_file()
    {
        var path = PathNamed("fresh.txt");

        await AtomicFile.WriteAsync(path, Bytes("created"), AtomicWriteMode.NeverOverwrite);

        Assert.Equal("created", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task The_stream_overload_copies_the_source_and_does_not_close_it()
    {
        var path = PathNamed("copy.bin");
        await using var source = new MemoryStream(Bytes("from a stream"));

        await AtomicFile.WriteAsync(path, source, AtomicWriteMode.Overwrite);

        Assert.Equal("from a stream", Text(path));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task A_writer_gets_a_stream_and_its_content_lands_on_success()
    {
        var path = PathNamed("two-parts.txt");

        await AtomicFile.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync(Bytes("one, "), ct);
            await stream.WriteAsync(Bytes("two"), ct);
        }, AtomicWriteMode.Overwrite);

        Assert.Equal("one, two", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task A_writer_that_throws_leaves_the_existing_file_untouched_and_no_temporary_file()
    {
        var path = PathNamed("settings.json");
        await AtomicFile.WriteAsync(path, Bytes("old"), AtomicWriteMode.Overwrite);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicFile.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync(Bytes("half"), ct);
            throw new InvalidOperationException("the source broke");
        }, AtomicWriteMode.Overwrite));

        Assert.Equal("the source broke", ex.Message);
        Assert.Equal("old", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task A_writer_that_throws_on_a_new_file_leaves_neither_file_nor_temporary_file()
    {
        var path = PathNamed("new.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicFile.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync(Bytes("half"), ct);
            throw new InvalidOperationException("the source broke");
        }, AtomicWriteMode.NeverOverwrite));

        Assert.False(File.Exists(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task A_write_cancelled_before_it_starts_leaves_the_target_untouched_and_no_temporary_file()
    {
        var path = PathNamed("settings.json");
        await AtomicFile.WriteAsync(path, Bytes("old"), AtomicWriteMode.Overwrite);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AtomicFile.WriteAsync(path, Bytes("new"), AtomicWriteMode.Overwrite, cancelled.Token));

        Assert.Equal("old", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task A_write_cancelled_part_way_removes_its_temporary_file()
    {
        var path = PathNamed("big.bin");
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync(new byte[1024], ct);
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }, AtomicWriteMode.Overwrite, cts.Token));

        Assert.False(File.Exists(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task A_folder_that_is_a_file_refuses_the_write_and_changes_nothing()
    {
        Directory.CreateDirectory(_folder);
        var blocker = PathNamed("blocker");
        await File.WriteAllTextAsync(blocker, "not a folder");
        var path = Path.Combine(blocker, "receipt.txt");

        await Assert.ThrowsAnyAsync<IOException>(() => AtomicFile.WriteAsync(path, Bytes("x"), AtomicWriteMode.Overwrite));

        Assert.Equal("not a folder", await File.ReadAllTextAsync(blocker));
    }

    [Fact]
    public async Task A_foreign_temporary_file_held_open_beside_the_target_does_not_block_a_write()
    {
        // Another copy of an application may hold a file with an old fixed temporary name open in the same folder. That file
        // must neither block this write nor be taken for this write's own temporary file.
        Directory.CreateDirectory(_folder);
        var target = PathNamed("gtfs.zip");
        var foreign = PathNamed("gtfs.zip.tmp");
        using var held = new FileStream(foreign, FileMode.Create, FileAccess.Write, FileShare.None);
        held.WriteByte(9);

        await AtomicFile.WriteAsync(target, Bytes("new"), AtomicWriteMode.Overwrite);

        Assert.Equal("new", Text(target));
        Assert.True(File.Exists(foreign));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_path_is_refused_when_the_write_is_called(string? path)
    {
        // The argument error is thrown by the call itself, before any task exists.
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            _ = AtomicFile.WriteAsync(path!, Bytes("x"), AtomicWriteMode.Overwrite);
        });
    }

    [Fact]
    public void A_root_is_refused_because_it_is_not_a_file()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        var ex = Assert.ThrowsAny<ArgumentException>(() =>
        {
            _ = AtomicFile.WriteAsync(root, Bytes("x"), AtomicWriteMode.Overwrite);
        });

        Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public void An_unknown_mode_is_refused_when_the_write_is_called()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = AtomicFile.WriteAsync(PathNamed("x.txt"), Bytes("x"), (AtomicWriteMode)99);
        });
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void A_missing_writer_or_stream_is_refused_when_the_write_is_called()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = AtomicFile.WriteAsync(PathNamed("x.txt"), (Func<Stream, CancellationToken, Task>)null!, AtomicWriteMode.Overwrite);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = AtomicFile.WriteAsync(PathNamed("x.txt"), (Stream)null!, AtomicWriteMode.Overwrite);
        });
    }

    [Fact]
    public async Task Concurrent_overwrites_never_mix_their_content()
    {
        var path = PathNamed("shared.txt");
        var contents = Enumerable.Range(0, 8).Select(i => new string((char)('a' + i), 200_000)).ToArray();

        // Replacing a file that another writer is replacing at the same moment can be refused by the file system: an IOException,
        // or on Windows an access-denied UnauthorizedAccessException. The file then keeps whichever complete version it had. The
        // check is that no version is ever mixed.
        var outcomes = await Task.WhenAll(contents.Select(async text =>
        {
            try
            {
                await AtomicFile.WriteAsync(path, Bytes(text), AtomicWriteMode.Overwrite);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }));

        Assert.Contains(true, outcomes);
        var final = await File.ReadAllTextAsync(path);
        Assert.Contains(contents, complete => complete == final);
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task Concurrent_create_new_writes_leave_exactly_one_winner()
    {
        var path = PathNamed("claimed.txt");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            try
            {
                await AtomicFile.WriteAsync(path, Bytes($"writer {i}"), AtomicWriteMode.NeverOverwrite);
                return (int?)i;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }));

        var winners = outcomes.Where(winner => winner is not null).ToArray();
        var won = Assert.Single(winners);
        Assert.Equal($"writer {won}", Text(path));
        Assert.Empty(TempFiles());
    }

    // -- the synchronous overloads: the same cases as the asynchronous ones ---------------------------------------------

    [Fact]
    public void Write_span_overwrites_an_existing_file_and_leaves_only_the_file()
    {
        var path = PathNamed("settings.json");
        AtomicFile.Write(path, Bytes("old"), AtomicWriteMode.Overwrite);

        AtomicFile.Write(path, Bytes("new"), AtomicWriteMode.Overwrite);

        Assert.Equal("new", Text(path));
        Assert.Equal("settings.json", Path.GetFileName(Assert.Single(Directory.GetFiles(_folder))));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Write_span_creates_the_folders_on_demand()
    {
        var path = Path.Combine(_folder, "uploads", "2026", "10", "photo.jpg");

        AtomicFile.Write(path, Bytes("x"), AtomicWriteMode.NeverOverwrite);

        Assert.Equal("x", Text(path));
    }

    [Fact]
    public void Write_span_never_overwrite_refuses_an_existing_file_and_leaves_it_untouched()
    {
        var path = PathNamed("receipt.txt");
        AtomicFile.Write(path, Bytes("first"), AtomicWriteMode.NeverOverwrite);

        var ex = Assert.Throws<IOException>(() => AtomicFile.Write(path, Bytes("second"), AtomicWriteMode.NeverOverwrite));

        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
        Assert.Equal("first", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Write_writer_fills_the_stream_and_the_content_lands_on_success()
    {
        var path = PathNamed("two-parts.txt");

        AtomicFile.Write(path, stream =>
        {
            stream.Write(Bytes("one, "));
            stream.Write(Bytes("two"));
        }, AtomicWriteMode.Overwrite);

        Assert.Equal("one, two", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Write_writer_that_throws_leaves_the_existing_file_untouched_and_no_temporary_file()
    {
        var path = PathNamed("settings.json");
        AtomicFile.Write(path, Bytes("old"), AtomicWriteMode.Overwrite);

        var ex = Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write(Bytes("half"));
            throw new InvalidOperationException("the source broke");
        }, AtomicWriteMode.Overwrite));

        Assert.Equal("the source broke", ex.Message);
        Assert.Equal("old", Text(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Write_writer_that_throws_on_a_new_file_leaves_neither_file_nor_temporary_file()
    {
        var path = PathNamed("new.txt");

        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write(Bytes("half"));
            throw new InvalidOperationException("the source broke");
        }, AtomicWriteMode.NeverOverwrite));

        Assert.False(File.Exists(path));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Write_a_folder_that_is_a_file_refuses_the_write_and_changes_nothing()
    {
        Directory.CreateDirectory(_folder);
        var blocker = PathNamed("blocker");
        File.WriteAllText(blocker, "not a folder");
        var path = Path.Combine(blocker, "receipt.txt");

        Assert.ThrowsAny<IOException>(() => AtomicFile.Write(path, Bytes("x"), AtomicWriteMode.Overwrite));

        Assert.Equal("not a folder", File.ReadAllText(blocker));
    }

    [Fact]
    public void Write_a_foreign_temporary_file_held_open_beside_the_target_does_not_block_a_write()
    {
        Directory.CreateDirectory(_folder);
        var target = PathNamed("gtfs.zip");
        var foreign = PathNamed("gtfs.zip.tmp");
        using var held = new FileStream(foreign, FileMode.Create, FileAccess.Write, FileShare.None);
        held.WriteByte(9);

        AtomicFile.Write(target, Bytes("new"), AtomicWriteMode.Overwrite);

        Assert.Equal("new", Text(target));
        Assert.True(File.Exists(foreign));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Write_refuses_a_blank_path_at_the_call(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => AtomicFile.Write(path!, Bytes("x"), AtomicWriteMode.Overwrite));
    }

    [Fact]
    public void Write_refuses_an_unknown_mode_and_a_missing_writer_at_the_call()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AtomicFile.Write(PathNamed("x.txt"), Bytes("x"), (AtomicWriteMode)99));
        Assert.Throws<ArgumentNullException>(() => AtomicFile.Write(PathNamed("x.txt"), (Action<Stream>)null!, AtomicWriteMode.Overwrite));
        Assert.Empty(TempFiles());
    }
}
