// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations.Tests;

public class TranslationMapTests
{
    private static readonly string[] ZetaAlphaMid = ["zeta", "alpha", "mid"];

    [Fact]
    public void Entries_keep_their_file_order()
    {
        var map = TestData.Map("en.json", ("zeta", "Z"), ("alpha", "A"), ("mid", "M"));

        Assert.Equal(ZetaAlphaMid, map.Entries.Select(entry => entry.Key));
        Assert.Equal(3, map.Count);
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void A_duplicate_key_is_reported_and_the_first_value_is_kept()
    {
        var map = new TranslationMap("en.json", [
            new TranslationEntry("save", "Save", 3),
            new TranslationEntry("cancel", "Cancel", 4),
            new TranslationEntry("save", "Save again", 9),
        ]);

        Assert.Equal(2, map.Count);
        Assert.True(map.TryGetValue("save", out var text));
        Assert.Equal("Save", text);

        var problem = Assert.Single(map.Problems);
        Assert.Equal(TranslationProblemKind.DuplicateKey, problem.Kind);
        Assert.Equal("save", problem.Key);
        Assert.Equal(9, problem.Line);
        Assert.Equal("en.json", problem.File);
        Assert.Equal("\"save\" appears more than once (first at line 3); the first value is used.", problem.Message);
    }

    [Fact]
    public void A_duplicate_without_lines_is_reported_without_a_line_note()
    {
        var map = TestData.Map("en.json", ("save", "Save"), ("save", "Save"));

        var problem = Assert.Single(map.Problems);
        Assert.Null(problem.Line);
        Assert.Equal("\"save\" appears more than once; the first value is used.", problem.Message);
    }

    [Fact]
    public void An_entry_with_a_blank_key_is_reported_and_not_kept()
    {
        var map = new TranslationMap("en.json", [new TranslationEntry(string.Empty, "x", 2), new TranslationEntry("  ", "y")]);

        Assert.Equal(0, map.Count);
        Assert.Equal(2, map.Problems.Count);
        Assert.All(map.Problems, problem => Assert.Equal(TranslationProblemKind.UnnamedEntry, problem.Kind));
        Assert.Equal(2, map.Problems[0].Line);
    }

    [Fact]
    public void Reader_problems_come_first_in_the_map_problems()
    {
        var earlier = new TranslationProblem("en.json", "old", 1, TranslationProblemKind.UnsupportedValue, "Earlier.");
        var map = new TranslationMap("en.json", [new TranslationEntry("a", "1"), new TranslationEntry("a", "2")], [earlier]);

        Assert.Equal(2, map.Problems.Count);
        Assert.Same(earlier, map.Problems[0]);
        Assert.Equal(TranslationProblemKind.DuplicateKey, map.Problems[1].Kind);
    }

    [Fact]
    public void Lookups_are_exact_and_a_miss_gives_an_empty_text()
    {
        var map = TestData.Map("en.json", ("save", "Save"));

        Assert.True(map.ContainsKey("save"));
        Assert.False(map.ContainsKey("Save"));
        Assert.False(map.TryGetValue("Save", out var missing));
        Assert.Equal(string.Empty, missing);
    }

    [Fact]
    public void The_constructor_refuses_a_blank_name_and_null_arguments()
    {
        Assert.Throws<ArgumentException>(() => new TranslationMap(" ", []));
        Assert.Throws<ArgumentNullException>(() => new TranslationMap("en.json", null!));
        Assert.Throws<ArgumentNullException>(() => new TranslationMap("en.json", [null!]));
        Assert.Throws<ArgumentNullException>(() => new TranslationMap("en.json", [new TranslationEntry("a", null!)]));
    }
}
