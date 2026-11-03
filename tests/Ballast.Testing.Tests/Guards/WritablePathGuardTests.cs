// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

using Ballast.Testing.Guards;
using Ballast.Testing.Guards.WritablePath;

namespace Ballast.Testing.Tests.Guards;

/// <summary>The writable-path rule: the reads of the content root, the allow-list around them, the retired names and the server-source reader.</summary>
public class WritablePathGuardTests
{
    private const string Reason = "Reviewed: the read is of the bundled files, and nothing is written there.";
    private const string StorePath = "ApiOne/Store.cs";

    private static WritablePathOptions Options(params string[] retired) => new() { ServerTrees = ["ApiOne"], RetiredNames = retired };

    private static IReadOnlyList<string> Check(string source, WritablePathOptions? options = null, params AllowEntry[] allow) =>
        WritablePathGuard.Problems([(StorePath, source)], options ?? Options(), allow);

    [Fact]
    public void A_read_of_the_content_root_with_no_entry_is_a_problem()
    {
        var problem = Assert.Single(Check("var root = builder.Environment.ContentRootPath;"));

        Assert.Equal(
            "WRITABLE-PATH-ALLOW-LIST: ApiOne/Store.cs:1: var root = builder.Environment.ContentRootPath; reads the content root or the app folder outside the allow-list. "
            + "Write under the data root instead, or add an entry with the reason the read is safe.",
            problem);
    }

    [Fact]
    public void A_source_with_no_read_of_the_content_root_passes()
    {
        Assert.Empty(Check("var p = Path.Combine(dataRoot, \"x\");"));
    }

    [Fact]
    public void A_comment_that_names_the_content_root_is_not_a_read()
    {
        Assert.Empty(Check("// the ContentRootPath is not read here\n/* WebRootPath */\nvar p = 1;"));
    }

    [Fact]
    public void An_entry_on_the_same_file_with_a_matching_fragment_excuses_the_read()
    {
        Assert.Empty(Check("var root = builder.Environment.ContentRootPath;", null, new AllowEntry("ApiOne/Store.cs|ContentRootPath", Reason)));
    }

    [Fact]
    public void An_entry_for_another_file_does_not_excuse_the_read_and_is_stale()
    {
        var problems = Check("var root = ContentRootPath;", null, new AllowEntry("ApiOne/Other.cs|ContentRootPath", Reason));

        Assert.Equal(2, problems.Count);
        Assert.Equal("WRITABLE-PATH-ALLOW-LIST ApiOne/Other.cs|ContentRootPath: stale, nothing it excuses is found. Remove the entry.", problems[0]);
        Assert.Contains("ApiOne/Store.cs:1:", problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_stale_entry_that_excuses_no_read_fails_even_when_the_source_is_clean()
    {
        var problem = Assert.Single(Check("var p = 1;", null, new AllowEntry("ApiOne/Store.cs|ContentRootPath", Reason)));

        Assert.Equal("WRITABLE-PATH-ALLOW-LIST ApiOne/Store.cs|ContentRootPath: stale, nothing it excuses is found. Remove the entry.", problem);
    }

    [Fact]
    public void An_entry_with_a_short_reason_excuses_nothing_so_the_read_fails_too()
    {
        var problems = Check("var root = ContentRootPath;", null, new AllowEntry("ApiOne/Store.cs|ContentRootPath", "too short"));

        Assert.Equal(2, problems.Count);
        Assert.Equal("WRITABLE-PATH-ALLOW-LIST ApiOne/Store.cs|ContentRootPath: the reason must be at least 20 characters.", problems[0]);
        Assert.Contains("ApiOne/Store.cs|ContentRootPath is not excused", problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_listed_twice_is_reported_and_the_first_entry_still_excuses()
    {
        var entry = new AllowEntry("ApiOne/Store.cs|ContentRootPath", Reason);

        var problem = Assert.Single(Check("var root = ContentRootPath;", null, entry, entry));

        Assert.Equal("WRITABLE-PATH-ALLOW-LIST ApiOne/Store.cs|ContentRootPath: listed more than once. Keep one entry.", problem);
    }

    [Theory]
    [InlineData("ApiOne/Store.cs")]
    [InlineData("ApiOne/Store.cs|")]
    [InlineData("|ContentRootPath")]
    public void A_key_that_is_not_path_bar_fragment_excuses_nothing(string key)
    {
        var problem = Assert.Single(Check("var p = 1;", null, new AllowEntry(key, Reason)));

        Assert.Equal($"WRITABLE-PATH-ALLOW-LIST {key}: the key must be path|line fragment, so it excuses nothing. Correct it.", problem);
    }

    [Fact]
    public void The_root_reads_are_an_option_and_the_default_is_not_used_when_replaced()
    {
        var options = new WritablePathOptions { ServerTrees = ["ApiOne"], RootUse = new Regex("MyRoot", RegexOptions.CultureInvariant) };

        Assert.Empty(Check("var r = ContentRootPath;", options));
        Assert.Single(Check("var r = MyRoot;", options));
    }

    [Fact]
    public void The_list_name_and_the_advice_are_options()
    {
        var options = new WritablePathOptions { ServerTrees = ["ApiOne"], ListName = "MY-LIST", Advice = "Ask the owner." };

        var problem = Assert.Single(Check("var r = ContentRootPath;", options));

        Assert.StartsWith("MY-LIST: ApiOne/Store.cs:1:", problem, StringComparison.Ordinal);
        Assert.EndsWith(" Ask the owner.", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_retired_name_is_found_even_in_a_comment_and_a_clean_source_has_none()
    {
        var problem = Assert.Single(WritablePathGuard.RetiredNameProblems([("ApiOne/Old.cs", "// custom-source.json was here")], Options("custom-source.json")));

        Assert.Equal(
            "WRITABLE-PATH-ALLOW-LIST: ApiOne/Old.cs contains the retired name 'custom-source.json', which must not appear in server code. Remove it.",
            problem);
        Assert.Empty(WritablePathGuard.RetiredNameProblems([("ApiOne/New.cs", "var x = 1;")], Options("custom-source.json")));
    }

    [Fact]
    public void The_planted_read_is_caught_and_a_clean_source_passes()
    {
        GuardSelfTest.AssertCatches(source => Check(source), "var r = WebRootPath;", "reads the content root");
        GuardSelfTest.AssertPasses(source => Check(source), "var r = Path.Combine(dataRoot, \"x\");");
    }

    [Fact]
    public void A_rule_that_sees_nothing_fails_its_own_self_test()
    {
        var blind = new WritablePathOptions { ServerTrees = ["ApiOne"], RootUse = new Regex("never-present-anywhere", RegexOptions.CultureInvariant) };

        Assert.ThrowsAny<Exception>(() => GuardSelfTest.AssertCatches(source => Check(source, blind), "var r = WebRootPath;", "reads the content root"));
    }

    [Fact]
    public void Server_sources_read_the_cs_files_of_the_trees_and_skip_build_output()
    {
        using var repo = new TempRepository();
        repo.Write("ApiOne/Store.cs", "class A {}");
        repo.Write("ApiOne/bin/Built.cs", "class B {}");
        repo.Write("ApiOne/notes.txt", "not code");

        var source = Assert.Single(WritablePathGuard.ServerSources(repo.Root, Options()));

        Assert.Equal("ApiOne/Store.cs", source.RelativePath);
        Assert.Equal("class A {}", source.Source);
    }

    [Fact]
    public void Server_sources_refuse_a_missing_tree_and_an_empty_tree_list()
    {
        using var repo = new TempRepository();

        Assert.Throws<DirectoryNotFoundException>(() => WritablePathGuard.ServerSources(repo.Root, Options()));
        Assert.Throws<ArgumentException>(() => WritablePathGuard.ServerSources(repo.Root, new WritablePathOptions { ServerTrees = [] }));
    }
}
