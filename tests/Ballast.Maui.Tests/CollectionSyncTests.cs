// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Ballast.Maui.Helpers;

namespace Ballast.Maui.Tests;

/// <summary>Reconciling a list in place: the result is exactly the target, and only what differs is touched.</summary>
public sealed class CollectionSyncTests
{
    private sealed class Item(string name)
    {
        public string Name { get; } = name;

        public override string ToString() => Name;
    }

    private static ObservableCollection<Item> Listing(params Item[] items) => new(items);

    [Fact]
    public void An_identical_target_changes_nothing()
    {
        var a = new Item("a");
        var b = new Item("b");
        var list = Listing(a, b);
        var events = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => events.Add(e.Action);

        CollectionSync.Align(list, [a, b]);

        Assert.Equal([a, b], list);
        Assert.Empty(events);
    }

    [Fact]
    public void A_new_item_is_inserted_without_touching_the_others()
    {
        var a = new Item("a");
        var c = new Item("c");
        var b = new Item("b");
        var list = Listing(a, c);
        var events = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => events.Add(e.Action);

        CollectionSync.Align(list, [a, b, c]);

        Assert.Equal([a, b, c], list);
        Assert.Equal([NotifyCollectionChangedAction.Add], events);
    }

    [Fact]
    public void An_item_the_target_dropped_is_removed()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var list = Listing(a, b, c);

        CollectionSync.Align(list, [a, c]);

        Assert.Equal([a, c], list);
    }

    [Fact]
    public void A_kept_item_out_of_position_is_moved_not_recreated()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var list = Listing(a, b, c);
        var events = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => events.Add(e.Action);

        CollectionSync.Align(list, [c, a, b]);

        Assert.Equal([c, a, b], list);
        Assert.All(events, action => Assert.Equal(NotifyCollectionChangedAction.Move, action));
    }

    [Fact]
    public void Inserts_removes_and_moves_together_end_on_the_target_order()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var d = new Item("d");
        var e = new Item("e");
        var list = Listing(a, b, c, d);

        CollectionSync.Align(list, [d, e, a, c]);

        Assert.Equal([d, e, a, c], list);
    }

    [Fact]
    public void An_empty_target_empties_the_list_and_an_empty_list_fills()
    {
        var a = new Item("a");
        var b = new Item("b");
        var list = Listing(a, b);

        CollectionSync.Align(list, []);
        Assert.Empty(list);

        CollectionSync.Align(list, [b, a]);
        Assert.Equal([b, a], list);
    }

    [Fact]
    public void A_replaced_item_is_a_new_row_at_the_same_place()
    {
        // A card that is not kept is swapped for a fresh one by the caller: same slot, new object.
        var a = new Item("a");
        var b = new Item("b");
        var b2 = new Item("b");
        var c = new Item("c");
        var list = Listing(a, b, c);

        CollectionSync.Align(list, [a, b2, c]);

        Assert.Equal([a, b2, c], list);
        Assert.Same(b2, list[1]);
    }
}
