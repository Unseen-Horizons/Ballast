// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Ads;

namespace Ballast.Maui.Tests;

/// <summary>
/// The page hook wraps content inside the handler's <c>Content</c> mapping, and every assignment to the page's content re-enters that mapping.
/// These drive the wrap with a fake page whose setter calls back into the hook the way the handler does, and pin that the wrap happens exactly once.
/// </summary>
public sealed class ContentWrapperTests
{
    private sealed class Node(string name)
    {
        public string Name { get; } = name;

        public List<Node> Children { get; } = [];
    }

    /// <summary>A page whose content setter re-enters the hook, as <c>Handler.UpdateValue("Content")</c> does.</summary>
    private sealed class FakePage
    {
        private readonly ContentWrapper<Node> _wrapper = new();
        private Node? _content;
        private int _depth;

        public int MappingCalls { get; private set; }

        public int WrapCalls { get; private set; }

        public int MaxDepth { get; private set; }

        public Node? Content
        {
            get => _content;
            set
            {
                _content = value;
                Map();
            }
        }

        /// <summary>What the prepended mapper does on every Content mapping.</summary>
        public void Map()
        {
            MappingCalls++;
            _depth++;
            MaxDepth = Math.Max(MaxDepth, _depth);

            // A real unguarded wrap would overflow the stack; fail the test cleanly instead.
            if (_depth > 50)
                throw new InvalidOperationException("The content mapping recursed without end.");

            try
            {
                _wrapper.EnsureWrapped(
                    () => _content,
                    content => Content = content,
                    original =>
                    {
                        WrapCalls++;
                        var layout = new Node("layout");
                        layout.Children.Add(original);
                        return layout;
                    });
            }
            finally
            {
                _depth--;
            }
        }

        public bool IsWrapped => _wrapper.IsWrapped;

        /// <summary>Sets the content without a mapping, as the constructor does before the handler exists.</summary>
        public void Seed(Node content) => _content = content;
    }

    [Fact]
    public void Content_set_before_the_handler_is_wrapped_once_with_the_original_inside()
    {
        var page = new FakePage();
        var original = new Node("original");

        page.Content = original;

        Assert.Equal(1, page.WrapCalls);
        Assert.True(page.IsWrapped);
        Assert.Equal("layout", page.Content!.Name);
        Assert.Same(original, Assert.Single(page.Content.Children));
    }

    [Fact]
    public void A_re_entered_mapping_during_the_wrap_is_a_no_op()
    {
        var page = new FakePage();
        var original = new Node("original");
        page.Seed(original);

        page.Map();

        Assert.Equal(1, page.WrapCalls);

        // Outer call, the re-entry from Content = null, and the re-entry from Content = layout.
        Assert.Equal(3, page.MappingCalls);
        Assert.Equal(2, page.MaxDepth);
        Assert.Equal("layout", page.Content!.Name);
        Assert.Same(original, Assert.Single(page.Content.Children));
        Assert.DoesNotContain(page.Content.Children, c => c.Name == "layout");
    }

    [Fact]
    public void Mapping_again_after_the_wrap_changes_nothing()
    {
        var page = new FakePage();
        page.Content = new Node("original");
        var wrapped = page.Content;

        page.Map();
        page.Map();

        Assert.Equal(1, page.WrapCalls);
        Assert.Same(wrapped, page.Content);
    }

    [Fact]
    public void A_page_with_no_content_is_not_wrapped_and_can_be_wrapped_later()
    {
        var wrapper = new ContentWrapper<Node>();
        Node? content = null;

        Assert.False(wrapper.EnsureWrapped(() => content, c => content = c, o => o));
        Assert.False(wrapper.IsWrapped);

        content = new Node("late");
        Assert.True(wrapper.EnsureWrapped(() => content, c => content = c, o => new Node("layout") { Children = { o } }));
        Assert.True(wrapper.IsWrapped);
    }

    [Fact]
    public void The_wrapper_is_recorded_before_it_is_assigned_as_content()
    {
        var wrapper = new ContentWrapper<Node>();
        Node? content = new Node("original");
        bool? wrappedWhenAssigned = null;

        wrapper.EnsureWrapped(
            () => content,
            c =>
            {
                if (c is { Name: "layout" })
                    wrappedWhenAssigned = wrapper.IsWrapped;
                content = c;
            },
            o => new Node("layout") { Children = { o } });

        Assert.True(wrappedWhenAssigned);
    }

    [Fact]
    public void A_failing_wrap_gives_the_page_its_own_content_back()
    {
        var wrapper = new ContentWrapper<Node>();
        var original = new Node("original");
        Node? content = original;

        Assert.Throws<InvalidOperationException>(() =>
            wrapper.EnsureWrapped(() => content, c => content = c, _ => throw new InvalidOperationException("boom")));

        Assert.Same(original, content);
        Assert.False(wrapper.IsWrapped);
    }
}
