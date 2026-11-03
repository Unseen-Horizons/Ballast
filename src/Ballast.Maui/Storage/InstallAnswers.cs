// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Storage;

/// <summary>
/// The answers this install gave to the first-start questions: they live in their own preferences container, which the app's
/// backup rules exclude. Android does not restore runtime permission grants, so an answer restored with a reinstall would skip a
/// question for a permission the phone no longer has.
/// </summary>
/// <remarks>
/// The keys stay with their owners in the app. Nothing is copied from the default preferences file: its old values are left alone
/// and no longer read, so an existing install sees the first-start questions once more, for what is still missing only.
/// <para>
/// Each call catches and logs a store that cannot be read or written, so a broken preference never stops a screen. What an
/// unreadable answer means is the caller's to say, which is why <see cref="Get{T}"/> takes it as an argument.
/// </para>
/// </remarks>
public sealed class InstallAnswers
{
    private readonly IPreferenceStore _preferences;
    private readonly IClientLog _log;

    /// <summary>Creates the view over one container.</summary>
    /// <param name="preferences">The preferences the answers are kept in.</param>
    /// <param name="sharedName">The container's name, for example <c>app.install</c> (the file <c>shared_prefs/app.install.xml</c> on Android).</param>
    /// <param name="log">Where an unreadable or unwritable answer is logged.</param>
    /// <exception cref="ArgumentException"><paramref name="sharedName"/> is blank.</exception>
    public InstallAnswers(IPreferenceStore preferences, string sharedName, IClientLog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedName);
        _preferences = preferences;
        SharedName = sharedName;
        _log = log;
    }

    /// <summary>The container's name.</summary>
    public string SharedName { get; }

    /// <summary>
    /// The stored value, or <paramref name="missing"/> when there is none. A container that cannot be read is logged and answers
    /// <paramref name="unreadable"/>.
    /// </summary>
    /// <typeparam name="T">A supported value type (see <see cref="IPreferenceStore"/>).</typeparam>
    /// <param name="key">The answer's key.</param>
    /// <param name="missing">The answer when none is stored.</param>
    /// <param name="unreadable">The answer when the container cannot be read.</param>
    /// <returns>The stored, missing or unreadable answer.</returns>
    public T Get<T>(string key, T missing, T unreadable)
    {
        try
        {
            return _preferences.Get(key, missing, SharedName);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(InstallAnswers), $"could not read {key} ({ex.GetType().Name})");
            return unreadable;
        }
    }

    /// <summary>Stores an answer. A container that cannot be written is logged.</summary>
    /// <typeparam name="T">A supported value type (see <see cref="IPreferenceStore"/>).</typeparam>
    /// <param name="key">The answer's key.</param>
    /// <param name="value">The answer.</param>
    public void Set<T>(string key, T value)
    {
        try
        {
            _preferences.Set(key, value, SharedName);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(InstallAnswers), $"could not save {key} ({ex.GetType().Name})");
        }
    }

    /// <summary>Removes an answer. A container that cannot be written is logged.</summary>
    /// <param name="key">The answer's key.</param>
    public void Remove(string key)
    {
        try
        {
            _preferences.Remove(key, SharedName);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(InstallAnswers), $"could not remove {key} ({ex.GetType().Name})");
        }
    }
}
