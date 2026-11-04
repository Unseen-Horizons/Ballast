// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations;

/// <summary>
/// An object of text values in the way JavaScript keeps one: a key keeps the place it was first given, and a later value
/// for the same key replaces the value only. Written out, the array-index keys come first in ascending order, as JavaScript writes them.
/// </summary>
internal sealed class OrderedStrings
{
    private readonly List<string> _keys = [];
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>The number of keys.</summary>
    public int Count => _keys.Count;

    /// <summary>
    /// Sets a key's value. A new key goes last; an existing key keeps its place.
    /// </summary>
    /// <param name="key">The key. Required.</param>
    /// <param name="value">The value. Required.</param>
    public void Set(string key, string value)
    {
        if (!_values.ContainsKey(key))
        {
            _keys.Add(key);
        }

        _values[key] = value;
    }

    /// <summary>
    /// Tells whether a key is in the object.
    /// </summary>
    /// <param name="key">The key. Required.</param>
    /// <returns>True when the key is present.</returns>
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>
    /// The properties in the order JavaScript gives them: array-index keys ascending, then the rest in the order they were set.
    /// </summary>
    /// <returns>The properties.</returns>
    public IEnumerable<KeyValuePair<string, string>> InJsOrder()
    {
        List<(uint Index, string Key)> indexed = [];
        List<string> named = [];
        foreach (var key in _keys)
        {
            if (JsText.IsArrayIndex(key, out var index))
            {
                indexed.Add((index, key));
            }
            else
            {
                named.Add(key);
            }
        }

        indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var (_, key) in indexed)
        {
            yield return new KeyValuePair<string, string>(key, _values[key]);
        }

        foreach (var key in named)
        {
            yield return new KeyValuePair<string, string>(key, _values[key]);
        }
    }
}
