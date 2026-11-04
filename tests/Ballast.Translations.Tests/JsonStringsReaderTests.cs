// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace Ballast.Translations.Tests;

public class JsonStringsReaderTests
{
    private static readonly string[] SaveAndCancel = ["save", "cancel"];
    private static readonly string[] SaveAndCancelTexts = ["Save", "Cancel"];
    private static readonly string[] NestedKeys = ["dialog.title", "dialog.buttons.close", "save"];
    private static readonly string[] SaveOnly = ["save"];
    private static readonly string[] NonStringMembers = ["n", "t", "f", "z", "a"];
    private static readonly string[] OkOnly = ["ok"];
    private static readonly string[] FixtureKeys = ["save", "cancel", "greeting", "dialog.title", "dialog.close"];

    [Fact]
    public void A_flat_object_reads_its_strings_in_file_order()
    {
        var map = TestData.Json("""{"save":"Save","cancel":"Cancel"}""");

        Assert.Equal(SaveAndCancel, map.Entries.Select(entry => entry.Key));
        Assert.Equal(SaveAndCancelTexts, map.Entries.Select(entry => entry.Value));
        Assert.All(map.Entries, entry => Assert.Null(entry.Line));
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void Nested_objects_are_flattened_with_a_dot_by_default()
    {
        var map = TestData.Json("""{"dialog":{"title":"Settings","buttons":{"close":"Close"}},"save":"Save"}""");

        Assert.Equal(NestedKeys, map.Entries.Select(entry => entry.Key));
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void A_given_separator_joins_the_flattened_keys()
    {
        var map = TestData.Json("""{"dialog":{"title":"Settings"}}""", separator: "/");

        Assert.Equal("dialog/title", map.Entries.Single().Key);
    }

    [Fact]
    public void An_object_with_no_members_adds_nothing_and_is_not_reported()
    {
        var map = TestData.Json("""{"empty":{},"save":"Save"}""");

        Assert.Equal(SaveOnly, map.Entries.Select(entry => entry.Key));
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void Values_that_are_not_strings_are_reported_and_not_read()
    {
        var map = TestData.Json("""{"n":1,"t":true,"f":false,"z":null,"a":[1]}""");

        Assert.Empty(map.Entries);
        Assert.Equal(NonStringMembers, map.Problems.Select(problem => problem.Key));
        Assert.All(map.Problems, problem => Assert.Equal(TranslationProblemKind.UnsupportedValue, problem.Kind));
        Assert.Equal("\"n\" is a number, not a string, so it is not read.", map.Problems[0].Message);
        Assert.Equal("\"t\" is a boolean, not a string, so it is not read.", map.Problems[1].Message);
        Assert.Equal("\"z\" is null, not a string, so it is not read.", map.Problems[3].Message);
        Assert.Equal("\"a\" is an array, not a string, so it is not read.", map.Problems[4].Message);
    }

    [Fact]
    public void A_flattened_key_that_collides_with_a_dotted_member_is_reported_as_a_duplicate()
    {
        var map = TestData.Json("""{"a":{"b":"1"},"a.b":"2"}""");

        Assert.Equal("1", map.Entries.Single().Value);
        var problem = Assert.Single(map.Problems);
        Assert.Equal(TranslationProblemKind.DuplicateKey, problem.Kind);
        Assert.Equal("a.b", problem.Key);
    }

    [Fact]
    public void A_member_name_repeated_in_one_object_is_reported_as_a_duplicate()
    {
        var map = TestData.Json("""{"save":"Save","save":"Save again"}""");

        Assert.Equal("Save", map.Entries.Single().Value);
        Assert.Equal(TranslationProblemKind.DuplicateKey, Assert.Single(map.Problems).Kind);
    }

    [Fact]
    public void A_member_with_an_empty_name_is_reported_and_its_value_is_not_read()
    {
        var map = TestData.Json("""{"":"x","ok":"y","group":{"":"z"}}""");

        Assert.Equal(OkOnly, map.Entries.Select(entry => entry.Key));
        Assert.Equal(2, map.Problems.Count);
        Assert.All(map.Problems, problem => Assert.Equal(TranslationProblemKind.UnnamedEntry, problem.Kind));
        Assert.Equal("A top-level member has no name, so it is not read.", map.Problems[0].Message);
        Assert.Equal("group", map.Problems[1].Key);
        Assert.Equal("A member under \"group\" has no name, so it is not read.", map.Problems[1].Message);
    }

    [Fact]
    public void A_root_that_is_not_an_object_is_refused()
    {
        var error = Assert.Throws<InvalidDataException>(() => TestData.Json("[\"save\"]", "list.json"));

        Assert.Equal("list.json is not a JSON object: its root must be an object whose members are strings.", error.Message);
    }

    [Fact]
    public void Malformed_json_is_refused_with_a_message_that_names_the_file()
    {
        var error = Assert.Throws<InvalidDataException>(() => TestData.Json("{\"save\": \"Save\"", "broken.json"));

        Assert.StartsWith("broken.json is not valid JSON:", error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_not_valid_json_here()
    {
        Assert.Throws<InvalidDataException>(() => TestData.Json("{\"save\": \"Save\" /* note */}"));
        Assert.Throws<InvalidDataException>(() => TestData.Json("{\"save\": \"Save\",}"));
    }

    [Fact]
    public void A_utf8_byte_order_mark_is_accepted()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"save\":\"Spremi\"}")).ToArray();

        var map = JsonStringsReader.Read(new MemoryStream(bytes), "bom.json");

        Assert.Equal("Spremi", map.Entries.Single().Value);
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void Blank_or_null_names_and_separators_are_refused()
    {
        Assert.Throws<ArgumentException>(() => JsonStringsReader.Read(TestData.Utf8("{}"), "x.json", " "));
        Assert.Throws<ArgumentException>(() => JsonStringsReader.Read(TestData.Utf8("{}"), " "));
        Assert.Throws<ArgumentNullException>(() => JsonStringsReader.Read(null!, "x.json"));
        Assert.Throws<ArgumentException>(() => JsonStringsReader.ReadFile(" "));
        Assert.Throws<ArgumentException>(() => JsonStringsReader.ReadFile(TestData.Fixture("messages.json"), string.Empty));
    }

    [Fact]
    public void A_missing_file_throws_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(() => JsonStringsReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "no-such.json")));
    }

    [Fact]
    public void A_file_is_read_and_named_with_the_path_it_was_given()
    {
        var path = TestData.Fixture("messages.json");

        var map = JsonStringsReader.ReadFile(path);

        Assert.Equal(path, map.Name);
        Assert.Equal(FixtureKeys, map.Entries.Select(entry => entry.Key));
        Assert.Empty(map.Problems);
    }
}
