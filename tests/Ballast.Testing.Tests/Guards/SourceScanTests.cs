// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Guards;

namespace Ballast.Testing.Tests.Guards;

public class SourceScanTests
{
    [Fact]
    public void A_line_comment_is_removed_and_its_line_break_kept()
    {
        var code = SourceScan.StripCSharpComments("int a = 1; // DateTime.Now\nint b = 2;");

        Assert.Equal("int a = 1; \nint b = 2;", code);
    }

    [Fact]
    public void A_block_comment_is_removed_and_its_line_breaks_are_kept_so_line_numbers_hold()
    {
        var source = "a();\n/* one\ntwo\nthree */b();";

        var code = SourceScan.StripCSharpComments(source);

        Assert.Equal("a();\n\n\nb();", code);
        Assert.Equal(source.Count(c => c == '\n'), code.Count(c => c == '\n'));
    }

    [Fact]
    public void A_double_slash_inside_a_string_literal_is_not_a_comment()
    {
        var code = SourceScan.StripCSharpComments("var url = \"https://example.test/a\"; // note");

        Assert.Equal("var url = \"https://example.test/a\"; ", code);
    }

    [Fact]
    public void An_escaped_quote_does_not_end_a_string_literal_early()
    {
        var code = SourceScan.StripCSharpComments("var s = \"say \\\"hi // there\\\"\"; // gone");

        Assert.Equal("var s = \"say \\\"hi // there\\\"\"; ", code);
    }

    [Fact]
    public void A_verbatim_literal_keeps_its_doubled_quotes_and_its_line_breaks()
    {
        var source = "var s = @\"a \"\"b // c\"\"\nd\"; // out";

        var code = SourceScan.StripCSharpComments(source);

        Assert.Equal("var s = @\"a \"\"b // c\"\"\nd\"; ", code);
    }

    [Fact]
    public void A_verbatim_literal_that_starts_with_three_quotes_is_not_a_raw_literal()
    {
        var code = SourceScan.StripCSharpComments("var s = @\"\"\"a\"\"\"; // after");

        Assert.Equal("var s = @\"\"\"a\"\"\"; ", code);
    }

    [Fact]
    public void A_raw_literal_keeps_its_slashes_and_closes_at_a_run_of_the_same_quote_count()
    {
        var source = "var s = \"\"\"\n  // not a comment \" still inside\n  \"\"\"; // gone";

        var code = SourceScan.StripCSharpComments(source);

        Assert.Equal("var s = \"\"\"\n  // not a comment \" still inside\n  \"\"\"; ", code);
    }

    [Fact]
    public void A_character_literal_of_a_quote_does_not_open_a_string()
    {
        var code = SourceScan.StripCSharpComments("char q = '\"'; // comment\nvar s = \"x\";");

        Assert.Equal("char q = '\"'; \nvar s = \"x\";", code);
    }

    [Fact]
    public void An_interpolated_literal_keeps_its_text_and_its_escapes()
    {
        var code = SourceScan.StripCSharpComments("var s = $\"{name} // kept \\\" \"; // gone");

        Assert.Equal("var s = $\"{name} // kept \\\" \"; ", code);
    }

    [Fact]
    public void An_unterminated_string_ends_at_its_line_break()
    {
        var code = SourceScan.StripCSharpComments("var s = \"open\nint b = 2; // gone");

        Assert.Equal("var s = \"open\nint b = 2; ", code);
    }

    [Fact]
    public void A_block_comment_that_never_closes_removes_the_rest_of_the_file_but_keeps_its_breaks()
    {
        var code = SourceScan.StripCSharpComments("a();\n/* never\nclosed");

        // The line break inside the unclosed comment is kept, so the line count of the source still holds.
        Assert.Equal("a();\n\n", code);
    }

    [Fact]
    public void Files_finds_only_the_wanted_extensions_and_skips_build_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ballast-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "deep"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "obj"));
        try
        {
            File.WriteAllText(Path.Combine(root, "src", "a.cs"), "class A { }");
            File.WriteAllText(Path.Combine(root, "src", "deep", "b.CS"), "class B { }");
            File.WriteAllText(Path.Combine(root, "src", "notes.txt"), "text");
            File.WriteAllText(Path.Combine(root, "bin", "built.cs"), "class Built { }");
            File.WriteAllText(Path.Combine(root, "obj", "gen.cs"), "class Gen { }");
            File.WriteAllText(Path.Combine(root, "src", "big.cs"), new string('x', 50));

            var found = SourceScan.Files(root, [".cs"], maxBytes: 20).Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToList();

            Assert.Equal(["src/a.cs", "src/deep/b.CS"], found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_scan_of_a_missing_folder_is_refused()
    {
        Assert.Throws<DirectoryNotFoundException>(() => SourceScan.Files(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")), [".cs"]).ToList());
    }
}
