// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Storage;

/// <summary>
/// Small typed preferences: a key and a value, optionally in a named container of their own. On a phone the default container
/// is the app's preferences file and a named one is its own file, so a backup rule can keep one and not the other.
/// </summary>
/// <remarks>
/// The supported value types are the ones MAUI's Preferences keeps: <c>string</c>, <c>bool</c>, <c>int</c>, <c>long</c>,
/// <c>float</c>, <c>double</c> and <c>DateTime</c>. Any other type throws when used. An implementation that cannot read or
/// write its store throws; a caller that must not fail catches and logs it (see <see cref="InstallAnswers"/>).
/// </remarks>
public interface IPreferenceStore
{
    /// <summary>The stored value, or <paramref name="defaultValue"/> when there is none.</summary>
    /// <typeparam name="T">A supported value type.</typeparam>
    /// <param name="key">The preference's key.</param>
    /// <param name="defaultValue">What to answer when nothing is stored under the key.</param>
    /// <param name="sharedName">The named container, or null for the default one.</param>
    T Get<T>(string key, T defaultValue, string? sharedName);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>.</summary>
    /// <typeparam name="T">A supported value type.</typeparam>
    /// <param name="key">The preference's key.</param>
    /// <param name="value">The value to keep.</param>
    /// <param name="sharedName">The named container, or null for the default one.</param>
    void Set<T>(string key, T value, string? sharedName);

    /// <summary>Removes the value under <paramref name="key"/>. Removing a key that is not there does nothing.</summary>
    /// <param name="key">The preference's key.</param>
    /// <param name="sharedName">The named container, or null for the default one.</param>
    void Remove(string key, string? sharedName);
}
