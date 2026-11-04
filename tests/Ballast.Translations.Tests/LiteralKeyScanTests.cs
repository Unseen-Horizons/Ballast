// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Translations.Tests;

public class LiteralKeyScanTests
{
    private static readonly TranslationMap Base = TestData.Map("en.json", ("save", "Save"), ("greeting", "Hello"));

    private static readonly Regex Ask = new("""ask\('(?<key>[a-z_]+)'\)""");

    [Fact]
    public void Every_use_is_counted_and_only_the_keys_missing_from_the_base_are_reported()
    {
        var result = LiteralKeyScan.Scan(Base, Ask, [("page.js", "ask('save');\nask('greeting');\nask('deleted');")]);

        Assert.Equal(3, result.Uses);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(TranslationProblemKind.UnknownKey, problem.Kind);
        Assert.Equal("page.js", problem.File);
        Assert.Equal("deleted", problem.Key);
        Assert.Equal(3, problem.Line);
        Assert.Equal("\"deleted\" is used here but is not in en.json.", problem.Message);
    }

    [Fact]
    public void A_scan_that_finds_nothing_reports_zero_uses_so_a_caller_can_require_a_minimum()
    {
        var result = LiteralKeyScan.Scan(Base, Ask, [("page.js", "no calls here")]);

        Assert.Equal(0, result.Uses);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Each_problem_carries_the_line_of_its_use()
    {
        var result = LiteralKeyScan.Scan(Base, Ask, [("f.js", "\n\n\nask('alpha');\n\nask('beta');\n")]);

        Assert.Equal(new int?[] { 4, 6 }, result.Problems.Select(problem => problem.Line));
    }

    [Fact]
    public void Alternatives_that_name_the_same_group_are_all_scanned()
    {
        var pattern = new Regex("""data-i18n="(?<key>[a-z_]+)"|__text\('(?<key>[a-z_]+)'\)""");

        var result = LiteralKeyScan.Scan(Base, pattern, [("p.html", "<b data-i18n=\"save\"></b>\n__text('nope')")]);

        Assert.Equal(2, result.Uses);
        Assert.Equal("nope", Assert.Single(result.Problems).Key);
    }

    [Fact]
    public void A_match_that_does_not_capture_the_key_is_not_a_use()
    {
        var pattern = new Regex("(?:ask\\('(?<key>[a-z_]+)'\\))|skip");

        var result = LiteralKeyScan.Scan(Base, pattern, [("f.js", "skip")]);

        Assert.Equal(0, result.Uses);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void A_pattern_without_the_key_group_is_refused()
    {
        var pattern = new Regex("ask\\('([a-z]+)'\\)");

        Assert.Throws<ArgumentException>(() => LiteralKeyScan.Scan(Base, pattern, []));
    }

    [Fact]
    public void A_different_group_name_can_be_given_and_is_then_the_only_one_read()
    {
        var pattern = new Regex("""ask\('(?<name>[a-z_]+)'\)""");

        var result = LiteralKeyScan.Scan(Base, pattern, [("f.js", "ask('save')")], "name");

        Assert.Equal(1, result.Uses);
        Assert.Empty(result.Problems);
        Assert.Throws<ArgumentException>(() => LiteralKeyScan.Scan(Base, pattern, [("f.js", "ask('save')")]));
        Assert.Throws<ArgumentException>(() => LiteralKeyScan.Scan(Base, pattern, [], " "));
    }

    [Fact]
    public void A_null_source_text_or_path_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => LiteralKeyScan.Scan(Base, Ask, [("f.js", null!)]));
        Assert.Throws<ArgumentNullException>(() => LiteralKeyScan.ScanFiles(Base, Ask, [null!]));
        Assert.Throws<ArgumentNullException>(() => LiteralKeyScan.Scan(null!, Ask, []));
    }

    [Fact]
    public void ScanFiles_reads_each_file_and_reports_it_by_its_path()
    {
        var path = Path.Combine(Path.GetTempPath(), "ballast-translations-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(path, "ask('save');\nask('gone');");
        try
        {
            var result = LiteralKeyScan.ScanFiles(Base, Ask, [path]);

            Assert.Equal(2, result.Uses);
            var problem = Assert.Single(result.Problems);
            Assert.Equal(path, problem.File);
            Assert.Equal("gone", problem.Key);
            Assert.Equal(2, problem.Line);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ScanFiles_throws_for_a_missing_file()
    {
        Assert.Throws<FileNotFoundException>(() =>
            LiteralKeyScan.ScanFiles(Base, Ask, [Path.Combine(AppContext.BaseDirectory, "no-such-page.js")]));
    }
}
