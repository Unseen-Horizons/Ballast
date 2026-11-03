// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;

namespace Ballast.Maui.Helpers;

/// <summary>
/// Brings an <see cref="ObservableCollection{T}"/> to a target order with the fewest changes, so a list that is reloaded with mostly the same items
/// moves, inserts and removes only what differs, instead of clearing and re-adding everything (which rebuilds every row).
/// </summary>
/// <remarks>
/// Pure and platform-free, so a test can pin it. Items are matched by reference: the caller decides which existing items to keep by putting those
/// same instances in the target.
/// </remarks>
public static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="list"/> hold exactly <paramref name="target"/>, in order. Items already in place are left alone; an item kept but out of
    /// position is moved; a new one is inserted; whatever the target no longer holds is removed. Must run on the thread that owns the collection.
    /// </summary>
    /// <typeparam name="T">The item type. Compared by reference.</typeparam>
    /// <param name="list">The collection to change.</param>
    /// <param name="target">The order it must end in.</param>
    public static void Align<T>(ObservableCollection<T> list, IReadOnlyList<T> target) where T : class
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);

        for (var i = 0; i < target.Count; i++)
        {
            var wanted = target[i];
            if (i < list.Count && ReferenceEquals(list[i], wanted))
                continue;

            var at = IndexOf(list, wanted, i + 1);
            if (at >= 0)
                list.Move(at, i);
            else
                list.Insert(i, wanted);
        }

        // Everything left past the target's length is an item the target dropped.
        while (list.Count > target.Count)
            list.RemoveAt(list.Count - 1);
    }

    /// <summary>
    /// The target to give <see cref="Align{T}"/> when the rows are rebuilt from source data on every load: for each source element, the row already in
    /// <paramref name="current"/> that still shows it (<paramref name="matches"/>), else a new one from <paramref name="create"/>. A row that is reused is
    /// the same object, so the list leaves it alone and its view is not rebuilt. Each current row is reused at most once, so two equal source elements
    /// never end up sharing a row.
    /// </summary>
    /// <typeparam name="TItem">The row type.</typeparam>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <param name="current">The rows now in the list.</param>
    /// <param name="source">The source elements, in the order the rows must show them.</param>
    /// <param name="matches">Whether a current row still shows a source element.</param>
    /// <param name="create">Makes a new row for an element no current row shows.</param>
    /// <returns>The rows in source order, reusing current rows where it can.</returns>
    /// <remarks>
    /// The row at the same position is tried first, which is the common case (a reload that changed nothing, or little) and keeps a 500-row list linear.
    /// </remarks>
    public static List<TItem> Reuse<TItem, TSource>(
        IReadOnlyList<TItem> current,
        IReadOnlyList<TSource> source,
        Func<TItem, TSource, bool> matches,
        Func<TSource, TItem> create) where TItem : class
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(create);

        var result = new List<TItem>(source.Count);
        var used = new bool[current.Count];
        for (var i = 0; i < source.Count; i++)
        {
            var element = source[i];
            TItem? found = null;
            if (i < current.Count && !used[i] && matches(current[i], element))
            {
                used[i] = true;
                found = current[i];
            }
            else
            {
                for (var j = 0; j < current.Count; j++)
                {
                    if (used[j] || !matches(current[j], element))
                        continue;

                    used[j] = true;
                    found = current[j];
                    break;
                }
            }

            result.Add(found ?? create(element));
        }

        return result;
    }

    private static int IndexOf<T>(ObservableCollection<T> list, T item, int from) where T : class
    {
        for (var i = from; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
                return i;
        }

        return -1;
    }
}
