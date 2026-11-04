// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Ballast.Maui.Diagnostics;
using Ballast.Storage;

namespace Ballast.Maui.Storage;

/// <summary>
/// On-device files kept per owner and sealed with the app's one key: one folder for each owner, each file a JSON document in
/// AES-GCM. The owner is hashed into the folder name, so two accounts, or an account and the guest who used the phone before
/// them, never see each other's files.
/// </summary>
/// <remarks>
/// <para>
/// A device is not a person, so the owner key comes from the app (a user id, or a guest key) and is hashed rather than used raw:
/// a user id in a path name leaks to anything that can list the folder, and it may hold characters a file system refuses.
/// </para>
/// <para>
/// A write goes beside its target and is moved into place (<see cref="AtomicFile"/>), so a process killed mid-write leaves the
/// previous file or none, never half of one. A write that fails is logged and reported as <c>false</c>: a cache that cannot be
/// written must not break the screen that was populating it. A read that fails, for a missing key, a tampered file or bad JSON,
/// answers <c>null</c> and is logged, which the caller shows as its ordinary "no cache".
/// </para>
/// <para>
/// The JSON is written and read through the <see cref="JsonTypeInfo{T}"/> the app passes in, so the library uses no reflection
/// and trimming keeps the types the app serialises.
/// </para>
/// </remarks>
public sealed class OwnedSealedStore : IDisposable
{
    private readonly OwnedStoreOptions _options;
    private readonly string _root;
    private readonly SealedKeyProvider _keys;
    private readonly IClientLog _log;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>Creates a store.</summary>
    /// <param name="options">The folder, extension and owner digits. Read once.</param>
    /// <param name="paths">The app's data folder, the store's parent.</param>
    /// <param name="keys">The app's one cache key.</param>
    /// <param name="log">Where a failed write or read is logged.</param>
    /// <exception cref="ArgumentException">The folder name is blank or has a path separator, or the extension is not a file extension.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="OwnedStoreOptions.OwnerDigits"/> is outside 16 to 64.</exception>
    public OwnedSealedStore(OwnedStoreOptions options, IAppPaths paths, SealedKeyProvider keys, IClientLog log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FolderName);
        if (options.FolderName.IndexOfAny(['/', '\\']) >= 0 || Path.GetFileName(options.FolderName) != options.FolderName)
            throw new ArgumentException("The folder name must be one folder name, not a path.", nameof(options));
        if (!options.FileExtension.StartsWith('.') || options.FileExtension.Length < 2 || options.FileExtension.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("The file extension must start with a dot and name no folder.", nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.OwnerDigits, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.OwnerDigits, 64);

        _options = options;
        _root = Path.Combine(paths.AppDataDirectory, options.FolderName);
        _keys = keys;
        _log = log;
    }

    /// <summary>The store's folder: every owner's folder is inside it.</summary>
    public string RootFolder => _root;

    /// <summary>The folder of one owner. Two owners that differ give different folders.</summary>
    /// <param name="ownerKey">The owner key the app builds (a user id, or a guest key). Blank is refused.</param>
    /// <returns>The owner's folder, inside <see cref="RootFolder"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="ownerKey"/> is null, empty or only white space.</exception>
    public string DirectoryFor(string ownerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerKey)))[.._options.OwnerDigits];
        return Path.Combine(_root, hash);
    }

    /// <summary>The file of one owner under one name.</summary>
    /// <param name="ownerKey">The owner key.</param>
    /// <param name="name">The file's name without its extension, for example <c>imported</c>. A name with a path separator is refused.</param>
    /// <returns>The file's full path.</returns>
    /// <exception cref="ArgumentException">The name is blank, or names more than one file.</exception>
    public string PathFor(string ownerKey, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Path.GetFileName(name) != name)
            throw new ArgumentException("A file name names one file, not a path.", nameof(name));
        return Path.Combine(DirectoryFor(ownerKey), name + _options.FileExtension);
    }

    /// <summary>
    /// Seals <paramref name="value"/> as JSON and writes it for one owner. The file appears whole or not at all.
    /// </summary>
    /// <typeparam name="T">The type written.</typeparam>
    /// <param name="ownerKey">The owner the file belongs to.</param>
    /// <param name="name">The file's name without its extension.</param>
    /// <param name="value">The value to keep.</param>
    /// <param name="typeInfo">The JSON type information for <typeparamref name="T"/>, from the app's source-generated context.</param>
    /// <param name="ct">Cancels the write. A cancelled write leaves the previous file as it was.</param>
    /// <returns>True when the file is written; false when it could not be (the failure is logged).</returns>
    public async Task<bool> SaveAsync<T>(string ownerKey, string name, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var path = PathFor(ownerKey, name);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = await _keys.GetKeyAsync(ct).ConfigureAwait(false);
            var sealed_ = await Task.Run(() => SealedPayload.SealBytes(key, JsonSerializer.SerializeToUtf8Bytes(value, typeInfo)), ct)
                .ConfigureAwait(false);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await AtomicFile.WriteAsync(path, sealed_, AtomicWriteMode.Overwrite, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Failure(nameof(OwnedSealedStore), $"could not save {name}", ex: ex);
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Reads and opens one owner's file. A file that is missing answers null without a log line; a file that cannot be opened or
    /// parsed answers null and is logged, and is left where it is.
    /// </summary>
    /// <typeparam name="T">The type read.</typeparam>
    /// <param name="ownerKey">The owner the file belongs to.</param>
    /// <param name="name">The file's name without its extension.</param>
    /// <param name="typeInfo">The JSON type information for <typeparamref name="T"/>.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The value, or null when there is no readable file.</returns>
    public async Task<T?> ReadAsync<T>(string ownerKey, string name, JsonTypeInfo<T> typeInfo, CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var path = PathFor(ownerKey, name);
        if (!File.Exists(path))
            return null;

        try
        {
            var key = await _keys.GetKeyAsync(ct).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return await Task.Run(() => JsonSerializer.Deserialize(SealedPayload.OpenBytes(key, bytes), typeInfo), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Corrupt, tampered with, or written under a key that no longer exists: all the same as absent to the caller, which
            // shows its ordinary offline state. A failed authentication tag lands here too, which is the point of the cipher.
            _log.Failure(nameof(OwnedSealedStore), $"could not read {name}; treated as absent", ex: ex);
            return null;
        }
    }

    /// <summary>
    /// Deletes everything held for one owner. Used when a session ends: the files are bearer credentials, so they do not outlive
    /// the session that fetched them. A failed delete is logged.
    /// </summary>
    /// <param name="ownerKey">The owner whose folder is removed.</param>
    public void Clear(string ownerKey)
    {
        try
        {
            var directory = DirectoryFor(ownerKey);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(nameof(OwnedSealedStore), $"could not clear the store ({ex.GetType().Name})");
        }
    }

    /// <summary>Whether any owner's files remain. The key may outlive a sign-out only while something is still sealed under it.</summary>
    /// <returns>True when the store's folder holds at least one entry.</returns>
    public bool HasAnyData() => Directory.Exists(_root) && Directory.EnumerateFileSystemEntries(_root).Any();

    /// <summary>Releases the write lock. Call it when the store's owner, usually the app's container, is torn down.</summary>
    public void Dispose() => _writeLock.Dispose();
}
