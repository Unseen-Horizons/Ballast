// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Storage;

/// <summary>
/// Secure-value reads that fail soft.
/// </summary>
/// <remarks>
/// Android Auto Backup and phone-to-phone transfers restore the settings file that names a secure value, but not the keystore
/// key that encrypts the value. On a restored phone a read can therefore throw. That must not reach the person as an error: the
/// value is treated as absent (a token missing means signed out; a missing cache key means a fresh one is made), the failure is
/// logged, and the secure store is cleared once, so the next sign-in writes values this phone can read. Nothing here substitutes
/// a value for the one that could not be read.
/// </remarks>
public sealed class SecureValueReader
{
    private readonly ISecureValues _values;
    private readonly IClientLog _log;
    private int _cleared;

    /// <summary>Creates a reader over one secure store.</summary>
    /// <param name="values">The secure store.</param>
    /// <param name="log">Where a failed read is logged.</param>
    public SecureValueReader(ISecureValues values, IClientLog log)
    {
        _values = values;
        _log = log;
    }

    /// <summary>
    /// The stored value, or null when it is absent or cannot be read. A read that fails is logged and clears the store once per
    /// process (see <see cref="ClearOnce"/>). Never throws, except that a cancelled read propagates its cancellation.
    /// </summary>
    /// <param name="key">The value's name.</param>
    /// <param name="ct">Cancels the read before it starts.</param>
    /// <returns>The value, or null.</returns>
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await _values.GetAsync(key, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(SecureValueReader), $"could not read {key}, treated as absent ({ex.GetType().Name})");
            ClearOnce();
            return null;
        }
    }

    /// <summary>
    /// Clears the whole secure store the first time a read fails in this process. A second failure only logs. A failed clear is
    /// logged and never thrown.
    /// </summary>
    public void ClearOnce()
    {
        if (Interlocked.Exchange(ref _cleared, 1) != 0)
            return;

        try
        {
            _values.RemoveAll();
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(SecureValueReader), $"could not clear the secure store ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// Re-arms <see cref="ClearOnce"/>, so the next failed read clears the store again. For tests, which share one process.
    /// </summary>
    public void RearmClear() => Interlocked.Exchange(ref _cleared, 0);
}
