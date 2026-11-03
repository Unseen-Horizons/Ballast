// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Ballast.Maui.Helpers;

namespace Ballast.Maui.Tests;

/// <summary>Rows rebuilt from source data on every load: a row that still shows its element is the same object, so Align leaves it alone.</summary>
public sealed class CollectionSyncReuseTests
{
    private sealed class Row(string key)
    {
        public string Key { get; } = key;
    }

    private static List<Row> Rebuild(IReadOnlyList<Row> current, IReadOnlyList<string> source) =>
        CollectionSync.Reuse(current, source, (row, key) => row.Key == key, key => new Row(key));

    [Fact]
    public void An_unchanged_source_yields_the_same_row_objects()
    {
        var a = new Row("a");
        var b = new Row("b");

        var result = Rebuild([a, b], ["a", "b"]);

        Assert.Same(a, result[0]);
        Assert.Same(b, result[1]);
    }

    [Fact]
    public void A_new_element_gets_a_new_row_and_the_others_are_reused()
    {
        var a = new Row("a");
        var c = new Row("c");

        var result = Rebuild([a, c], ["a", "b", "c"]);

        Assert.Same(a, result[0]);
        Assert.Equal("b", result[1].Key);
        Assert.NotSame(c, result[1]);
        Assert.Same(c, result[2]);
    }

    [Fact]
    public void A_reordered_source_reuses_rows_found_at_another_position()
    {
        var a = new Row("a");
        var b = new Row("b");
        var c = new Row("c");

        var result = Rebuild([a, b, c], ["c", "a", "b"]);

        Assert.Same(c, result[0]);
        Assert.Same(a, result[1]);
        Assert.Same(b, result[2]);
    }

    [Fact]
    public void Two_equal_elements_never_share_one_row()
    {
        var a = new Row("a");

        var result = Rebuild([a], ["a", "a"]);

        Assert.Equal(2, result.Count);
        Assert.NotSame(result[0], result[1]);
        Assert.Contains(a, result);
    }

    [Fact]
    public void A_row_whose_element_is_gone_is_not_in_the_result()
    {
        var a = new Row("a");
        var b = new Row("b");

        var result = Rebuild([a, b], ["b"]);

        Assert.Single(result);
        Assert.Same(b, result[0]);
    }

    [Fact]
    public void Reuse_then_align_touches_only_what_changed()
    {
        var a = new Row("a");
        var b = new Row("b");
        var c = new Row("c");
        var list = new ObservableCollection<Row>([a, b, c]);
        var events = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => events.Add(e.Action);

        // The same three, reloaded: no event at all, so no row is rebuilt.
        CollectionSync.Align(list, Rebuild(list, ["a", "b", "c"]));
        Assert.Empty(events);
        Assert.Same(a, list[0]);

        // One new element between: a single insert, the rest untouched.
        CollectionSync.Align(list, Rebuild(list, ["a", "x", "b", "c"]));
        Assert.Equal([NotifyCollectionChangedAction.Add], events);
        Assert.Same(a, list[0]);
        Assert.Same(b, list[2]);
        Assert.Same(c, list[3]);
    }

    [Fact]
    public void An_empty_source_yields_no_rows_and_an_empty_current_yields_new_ones()
    {
        Assert.Empty(Rebuild([new Row("a")], []));

        var result = Rebuild([], ["a", "b"]);

        Assert.Equal(["a", "b"], result.Select(r => r.Key));
    }
}
