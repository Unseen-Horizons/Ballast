// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>
/// Writes a file so that it appears whole or not at all. The content goes first to a temporary file in the same folder. That
/// file is flushed to disk and then moved over the final name, which is a single rename, so a reader sees the old file or the
/// new one and never half of either. A write that fails, or is cancelled, removes its temporary file and leaves the final name
/// as it was.
/// </summary>
/// <remarks>
/// Every write takes a temporary name of its own: 32 lower-case hex digits and <c>.atomic-tmp</c>. So two writers, and two copies
/// of an application that share a folder during a deploy, never share a temporary file. A temporary file that a crash leaves
/// behind is removed by <see cref="SweepStale"/>, which recognises only that name. The asynchronous and the synchronous overloads
/// share one rule for the temporary file, the move and the cleanup.
/// </remarks>
public static class AtomicFile
{
    /// <summary>
    /// The key under which a failed write records a temporary file it could not remove, in <see cref="Exception.Data"/>. The
    /// value is the file's path and the reason. The failure itself is still the exception that is thrown.
    /// </summary>
    public const string TempFileNotRemovedKey = "Ballast.Storage.TempFileNotRemoved";

    /// <summary>The suffix of every temporary file, after 32 lower-case hex digits.</summary>
    private const string TempSuffix = ".atomic-tmp";

    private const int TempNameDigits = 32;
    private const int BufferSize = 81920;

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> as one file.
    /// </summary>
    /// <param name="path">The final file path. Its folder is created when it does not exist.</param>
    /// <param name="content">The bytes to write.</param>
    /// <param name="mode">What to do when the file already exists.</param>
    /// <param name="ct">Cancels the write. A cancelled write removes its temporary file and leaves the target as it was.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or names a root, not a file.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a value of <see cref="AtomicWriteMode"/>.</exception>
    /// <exception cref="IOException">
    /// The file exists and <paramref name="mode"/> is <see cref="AtomicWriteMode.NeverOverwrite"/>, or the file system refuses the
    /// write or the move (for example, another process holds the target open in a way that blocks replacing it).
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The file system denies the move or the write: a read-only target, or on Windows a replacement that another writer is making
    /// at the same moment. The old file is left as it was.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the write finished.</exception>
    public static Task WriteAsync(string path, ReadOnlyMemory<byte> content, AtomicWriteMode mode, CancellationToken ct = default)
    {
        return WriteAsync(path, (stream, token) => stream.WriteAsync(content, token).AsTask(), mode, ct);
    }

    /// <summary>
    /// Copies <paramref name="content"/> from its current position to the end into <paramref name="path"/> as one file. The
    /// source stream is read and not disposed.
    /// </summary>
    /// <param name="path">The final file path. Its folder is created when it does not exist.</param>
    /// <param name="content">The stream to copy from.</param>
    /// <param name="mode">What to do when the file already exists.</param>
    /// <param name="ct">Cancels the copy. A cancelled copy removes its temporary file and leaves the target as it was.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or names a root, not a file.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a value of <see cref="AtomicWriteMode"/>.</exception>
    /// <exception cref="IOException">As for <see cref="WriteAsync(string, ReadOnlyMemory{byte}, AtomicWriteMode, CancellationToken)"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the copy finished.</exception>
    public static Task WriteAsync(string path, Stream content, AtomicWriteMode mode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return WriteAsync(path, (stream, token) => content.CopyToAsync(stream, token), mode, ct);
    }

    /// <summary>
    /// Writes a file by letting <paramref name="writeContent"/> fill a temporary stream that this method gives it. The caller
    /// writes into the stream and must not dispose it: this method flushes it to disk, closes it and then moves the file into
    /// place.
    /// </summary>
    /// <param name="path">The final file path. Its folder is created when it does not exist.</param>
    /// <param name="writeContent">Writes the content into the stream it is given. An exception it throws is the write's failure.</param>
    /// <param name="mode">What to do when the file already exists.</param>
    /// <param name="ct">Cancels the write. It is also passed to <paramref name="writeContent"/>.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="writeContent"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or names a root, not a file.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a value of <see cref="AtomicWriteMode"/>.</exception>
    /// <exception cref="IOException">As for <see cref="WriteAsync(string, ReadOnlyMemory{byte}, AtomicWriteMode, CancellationToken)"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the write finished.</exception>
    /// <remarks>
    /// Argument errors are thrown when the method is called. Every other failure, and the cancellation, is thrown by the
    /// returned task, and the write's exception is the one thrown, unchanged.
    /// </remarks>
    public static Task WriteAsync(string path, Func<Stream, CancellationToken, Task> writeContent, AtomicWriteMode mode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writeContent);
        var (target, directory) = ValidTarget(path, mode);
        return WriteCoreAsync(target, directory, writeContent, mode, ct);
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> as one file, and returns when the file is in place. It is the
    /// synchronous form of <see cref="WriteAsync(string, ReadOnlyMemory{byte}, AtomicWriteMode, CancellationToken)"/>, with the same
    /// temporary file, flush, move and cleanup.
    /// </summary>
    /// <param name="path">The final file path. Its folder is created when it does not exist.</param>
    /// <param name="content">The bytes to write.</param>
    /// <param name="mode">What to do when the file already exists.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or names a root, not a file.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a value of <see cref="AtomicWriteMode"/>.</exception>
    /// <exception cref="IOException">As for <see cref="WriteAsync(string, ReadOnlyMemory{byte}, AtomicWriteMode, CancellationToken)"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">As for <see cref="WriteAsync(string, ReadOnlyMemory{byte}, AtomicWriteMode, CancellationToken)"/>.</exception>
    public static void Write(string path, ReadOnlySpan<byte> content, AtomicWriteMode mode)
    {
        var (target, directory) = ValidTarget(path, mode);
        WriteCore(target, directory, mode, content, null);
    }

    /// <summary>
    /// Writes a file by letting <paramref name="writeContent"/> fill a temporary stream that this method gives it, and returns when
    /// the file is in place. It is the synchronous form of the writer overload of
    /// <see cref="WriteAsync(string, Func{Stream, CancellationToken, Task}, AtomicWriteMode, CancellationToken)"/>. The caller must
    /// not dispose the stream.
    /// </summary>
    /// <param name="path">The final file path. Its folder is created when it does not exist.</param>
    /// <param name="writeContent">Writes the content into the stream it is given. An exception it throws is the write's failure.</param>
    /// <param name="mode">What to do when the file already exists.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writeContent"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or names a root, not a file.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a value of <see cref="AtomicWriteMode"/>.</exception>
    /// <exception cref="IOException">As for <see cref="WriteAsync(string, ReadOnlyMemory{byte}, AtomicWriteMode, CancellationToken)"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">As for <see cref="WriteAsync(string, ReadOnlyMemory{byte}, AtomicWriteMode, CancellationToken)"/>.</exception>
    /// <remarks>
    /// Argument errors are thrown when the method is called. Every other failure is thrown by the method, and the writer's exception
    /// is the one thrown, unchanged. This form has no cancellation token.
    /// </remarks>
    public static void Write(string path, Action<Stream> writeContent, AtomicWriteMode mode)
    {
        ArgumentNullException.ThrowIfNull(writeContent);
        var (target, directory) = ValidTarget(path, mode);
        WriteCore(target, directory, mode, ReadOnlySpan<byte>.Empty, writeContent);
    }

    /// <summary>
    /// Removes the temporary files that a crash left in <paramref name="folder"/>. It looks only at files whose name is the
    /// temporary name this class gives (32 lower-case hex digits and <c>.atomic-tmp</c>), in that folder only, not in its
    /// subfolders. Any other file is never touched, even one with a similar name.
    /// </summary>
    /// <param name="folder">The folder to sweep.</param>
    /// <param name="olderThan">
    /// A temporary file is removed only when its last write is at least this old. Choose a value longer than the longest write into the
    /// folder: a file that a live writer is still filling is young, and must not be removed.
    /// </param>
    /// <returns>
    /// What was removed, and the files that were old but could not be removed, each with its error. A failed removal is reported
    /// here and is not thrown.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="olderThan"/> is negative.</exception>
    /// <exception cref="IOException">The folder exists but cannot be listed.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder exists but the caller may not list it.</exception>
    /// <remarks>
    /// A folder that does not exist has nothing to sweep, so the result is empty and no error is raised.
    /// </remarks>
    public static AtomicSweepResult SweepStale(string folder, TimeSpan olderThan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (olderThan < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(olderThan), olderThan, "The age must not be negative.");
        }

        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
        {
            return new AtomicSweepResult([], []);
        }

        var cutoff = DateTime.UtcNow - olderThan;
        List<string> removed = [];
        List<AtomicSweepFailure> failed = [];
        foreach (var file in Directory.GetFiles(full))
        {
            if (!IsTempName(Path.GetFileName(file)))
            {
                continue;
            }

            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff)
                {
                    continue;
                }

                File.Delete(file);
                removed.Add(file);
            }
            catch (FileNotFoundException)
            {
                // Gone since the listing: another sweep or a finishing write removed it. Nothing is left to report.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(new AtomicSweepFailure(file, ex));
            }
        }

        return new AtomicSweepResult(removed, failed);
    }

    /// <summary>
    /// Checks the path and the mode, and returns the full target path with its folder. A mode that is set to create-new is
    /// refused here when the file already exists, before anything is written.
    /// </summary>
    private static (string Target, string Directory) ValidTarget(string path, AtomicWriteMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (mode is not (AtomicWriteMode.Overwrite or AtomicWriteMode.NeverOverwrite))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The write mode is not a value of AtomicWriteMode.");
        }

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target);
        if (directory is null)
        {
            throw new ArgumentException("The path names a root, not a file.", nameof(path));
        }

        if (mode == AtomicWriteMode.NeverOverwrite && File.Exists(target))
        {
            throw AlreadyExists(target, null);
        }

        return (target, directory);
    }

    private static async Task WriteCoreAsync(string target, string directory, Func<Stream, CancellationToken, Task> writeContent, AtomicWriteMode mode, CancellationToken ct)
    {
        var (temp, stream) = OpenTemp(directory, useAsync: true);
        try
        {
            // The stream is closed before the move, because Windows will not move a file that is still open.
            await using (stream)
            {
                await writeContent(stream, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            MoveIntoPlace(temp, target, mode);
        }
        catch (Exception ex)
        {
            RemoveTemp(temp, ex);
            throw;
        }
    }

    /// <summary>
    /// The synchronous write. It is given either the bytes to write (<paramref name="writeContent"/> is null) or a writer (the bytes
    /// are then empty). A span cannot be captured by a delegate, so the two are kept apart here.
    /// </summary>
    private static void WriteCore(string target, string directory, AtomicWriteMode mode, ReadOnlySpan<byte> content, Action<Stream>? writeContent)
    {
        var (temp, stream) = OpenTemp(directory, useAsync: false);
        try
        {
            using (stream)
            {
                if (writeContent is null)
                {
                    stream.Write(content);
                }
                else
                {
                    writeContent(stream);
                }

                stream.Flush(flushToDisk: true);
            }

            MoveIntoPlace(temp, target, mode);
        }
        catch (Exception ex)
        {
            RemoveTemp(temp, ex);
            throw;
        }
    }

    /// <summary>
    /// Creates the temporary file for one write, in the target's folder. It is created before the caller's try block, so when this
    /// fails there is no temporary file of this write to remove.
    /// </summary>
    private static (string Temp, FileStream Stream) OpenTemp(string directory, bool useAsync)
    {
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, NewTempName());
        var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync);
        return (temp, stream);
    }

    /// <summary>
    /// Moves the finished temporary file over the target. Under create-new the move fails when the target exists, so a second
    /// writer that got past the check at the start is refused the same way.
    /// </summary>
    private static void MoveIntoPlace(string temp, string target, AtomicWriteMode mode)
    {
        if (mode == AtomicWriteMode.Overwrite)
        {
            File.Move(temp, target, overwrite: true);
            return;
        }

        try
        {
            File.Move(temp, target, overwrite: false);
        }
        catch (IOException ex) when (File.Exists(target))
        {
            throw AlreadyExists(target, ex);
        }
    }

    /// <summary>
    /// Removes a temporary file after a failed write. The failure stays the exception that is thrown: when the removal fails too,
    /// the leftover is recorded on that exception, and <see cref="SweepStale"/> removes it later.
    /// </summary>
    private static void RemoveTemp(string temp, Exception failure)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
        {
            failure.Data[TempFileNotRemovedKey] = $"{temp}: {cleanup.Message}";
        }
    }

    private static IOException AlreadyExists(string target, Exception? inner) =>
        new($"The file already exists, and {nameof(AtomicWriteMode.NeverOverwrite)} leaves it untouched: {target}", inner);

    private static string NewTempName() => Guid.NewGuid().ToString("N") + TempSuffix;

    /// <summary>True when the name is one this class gives a temporary file: 32 lower-case hex digits, then the suffix.</summary>
    private static bool IsTempName(string name)
    {
        if (name.Length != TempNameDigits + TempSuffix.Length || !name.EndsWith(TempSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 0; i < TempNameDigits; i++)
        {
            var c = name[i];
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
