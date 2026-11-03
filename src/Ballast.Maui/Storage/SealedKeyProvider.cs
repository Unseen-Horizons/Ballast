// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;

namespace Ballast.Maui.Storage;

/// <summary>
/// The one cache key of an app's on-device stores: it makes the key on first use, keeps it in the secure store, and hands the
/// same key to every store.
/// </summary>
/// <remarks>
/// Every store seals its files with one key. If two callers held different keys, whichever wrote last would leave the other's
/// file undecryptable. Two writes that happen back to back are one lock: without it both could find no key, generate different
/// ones, and the second would overwrite the first.
/// <para>
/// A key that cannot be read counts as no key: a fresh one is made, and whatever was sealed under the old one is treated as
/// absent by the stores. A stored key that is not valid base64 throws <see cref="FormatException"/>, which the caller treats
/// as unreadable; it is never replaced by a guess.
/// </para>
/// </remarks>
public sealed class SealedKeyProvider : IDisposable
{
    /// <summary>The key length in bytes: 256 bits, for AES-256-GCM.</summary>
    public const int KeyBytes = 32;

    private readonly ISecureValues _values;
    private readonly SecureValueReader _reader;
    private readonly string _keyName;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private byte[]? _cachedKey;

    /// <summary>Creates a provider for one key name.</summary>
    /// <param name="values">The secure store the key is kept in.</param>
    /// <param name="reader">The fail-soft reader used for the stored key.</param>
    /// <param name="keyName">The name the key is stored under. Each app names its own; the library has no default.</param>
    /// <exception cref="ArgumentException"><paramref name="keyName"/> is null, empty or only white space.</exception>
    public SealedKeyProvider(ISecureValues values, SecureValueReader reader, string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        _values = values;
        _reader = reader;
        _keyName = keyName;
    }

    /// <summary>The name the key is stored under.</summary>
    public string KeyName => _keyName;

    /// <summary>
    /// The key, read from the secure store or made and stored on first use. Callers asking at the same moment get the same key,
    /// and the key is written once.
    /// </summary>
    /// <param name="ct">Cancels the wait for the lock.</param>
    /// <returns>The 32-byte key.</returns>
    /// <exception cref="FormatException">The stored key is not valid base64.</exception>
    public async Task<byte[]> GetKeyAsync(CancellationToken ct = default)
    {
        if (_cachedKey is not null)
            return _cachedKey;

        await _keyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedKey is not null)
                return _cachedKey;

            var stored = await _reader.GetAsync(_keyName, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                _cachedKey = Convert.FromBase64String(stored);
                return _cachedKey;
            }

            var key = RandomNumberGenerator.GetBytes(KeyBytes);
            await _values.SetAsync(_keyName, Convert.ToBase64String(key), ct).ConfigureAwait(false);
            _cachedKey = key;
            return key;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    /// <summary>
    /// Deletes the key from the secure store and from memory. Whatever is still sealed under it becomes unreadable, so callers
    /// delete the files first. The next <see cref="GetKeyAsync"/> makes a fresh key.
    /// </summary>
    /// <param name="ct">Cancels the wait for the lock.</param>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _keyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _values.Remove(_keyName);
            _cachedKey = null;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    /// <summary>Releases the lock.</summary>
    public void Dispose() => _keyLock.Dispose();
}
