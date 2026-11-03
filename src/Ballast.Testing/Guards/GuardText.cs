// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Testing.Guards;

/// <summary>Small helpers the guard engines share.</summary>
internal static class GuardText
{
    /// <summary>
    /// The items of <paramref name="have"/> that <paramref name="want"/> does not account for, counting repeats: an item listed twice in
    /// <paramref name="want"/> must be found twice in <paramref name="have"/>.
    /// </summary>
    /// <param name="have">The items found in the code.</param>
    /// <param name="want">The items the rule expects.</param>
    internal static List<string> Surplus(IEnumerable<string> have, IEnumerable<string> want)
    {
        var counts = want
            .GroupBy(item => item, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        List<string> surplus = [];
        foreach (var item in have)
        {
            if (counts.TryGetValue(item, out var left) && left > 0)
            {
                counts[item] = left - 1;
            }
            else
            {
                surplus.Add(item);
            }
        }

        return surplus;
    }
}
