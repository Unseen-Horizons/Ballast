// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Auth;

/// <summary>Turns a user key into the text a claim or an audit row carries, the same way for a string key and an int key.</summary>
internal static class AuthKey
{
    /// <summary>The key as invariant text: a number has no thousands separator and no culture-specific digits.</summary>
    /// <typeparam name="TKey">The key type: a string or an integer, or a GUID.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The key's text, never null.</returns>
    public static string Text<TKey>(TKey key)
    {
        object? boxed = key;
        if (boxed is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return boxed?.ToString() ?? string.Empty;
    }
}
