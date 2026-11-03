// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Storage;

/// <summary>
/// The device's secure value store: a string per name, kept in the platform keystore on a phone (MAUI's SecureStorage). The
/// names are not secret; the values are. Nothing here is a cache: a value that is read back is the value that was written.
/// </summary>
public interface ISecureValues
{
    /// <summary>The stored value for <paramref name="key"/>, or null when there is none. A store that cannot be read throws.</summary>
    /// <param name="key">The value's name.</param>
    /// <param name="ct">Cancels the read before it starts. The platform store takes no token once started.</param>
    /// <returns>The value, or null when nothing is stored under the name.</returns>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>, replacing any value already there.</summary>
    /// <param name="key">The value's name.</param>
    /// <param name="value">The value to keep.</param>
    /// <param name="ct">Cancels the write before it starts. The platform store takes no token once started.</param>
    Task SetAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Removes one value. True when a value was there and is now gone.</summary>
    /// <param name="key">The value's name.</param>
    bool Remove(string key);

    /// <summary>Removes every value this app keeps in the store.</summary>
    void RemoveAll();
}
