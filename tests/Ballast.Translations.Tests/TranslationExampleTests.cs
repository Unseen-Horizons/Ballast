// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Translations.Tests;

/// <summary>
/// The whole flow over small fixture files: a .resx pair, a JSON pair and a page that asks for keys. It is the shape an app's test
/// takes, with the files read from disk.
/// </summary>
public class TranslationExampleTests
{
    private static readonly string[] BrokenKeys = ["cancel", "greeting", "item_count", "unit"];

    [Fact]
    public void A_resx_pair_is_in_parity_with_its_same_text_allow_list()
    {
        var english = ResxReader.ReadFile(TestData.Fixture("Strings.resx"));
        var croatian = ResxReader.ReadFile(TestData.Fixture("Strings.hr.resx"));
        var options = new TranslationParityOptions { SameTextAllowList = ["unit"] };

        var problems = TranslationParity.Check(english, croatian, options);

        Assert.Empty(problems);
        Assert.Equal(7, english.Count);
        Assert.Equal(7, croatian.Count);
    }

    [Fact]
    public void A_json_pair_is_in_parity()
    {
        var english = JsonStringsReader.ReadFile(TestData.Fixture("messages.json"));
        var croatian = JsonStringsReader.ReadFile(TestData.Fixture("messages.hr.json"));

        Assert.Empty(TranslationParity.Check(english, croatian));
    }

    [Fact]
    public void A_page_asks_for_keys_that_the_english_resx_holds_except_the_one_it_should_not()
    {
        var english = ResxReader.ReadFile(TestData.Fixture("Strings.resx"));
        var pattern = new Regex("""data-i18n="(?<key>[a-z_]+)"|__text\('(?<key>[a-z_]+)'\)""");

        var result = LiteralKeyScan.ScanFiles(english, pattern, [TestData.Fixture("page.html")]);

        Assert.Equal(3, result.Uses);
        var problem = Assert.Single(result.Problems);
        Assert.Equal("deleted_key", problem.Key);
        Assert.Equal(TestData.Fixture("page.html"), problem.File);
        Assert.Equal(7, problem.Line);
    }

    [Fact]
    public void A_broken_translation_pair_reports_each_problem_with_its_file_and_line()
    {
        var english = ResxReader.ReadFile(TestData.Fixture("Strings.resx"));
        var broken = TestData.Resx(
            "<data name=\"save\"><value>Spremi</value></data>\n"
            + "<data name=\"cancel\"><value></value></data>\n"
            + "<data name=\"greeting\"><value>Bok, {ime}!</value></data>\n"
            + "<data name=\"item_count\"><value>{0} stavki {1}</value></data>\n"
            + "<data name=\"price\"><value>Cijena {0:N2} EUR</value></data>\n"
            + "<data name=\"title\"><value>Postavke</value></data>\n"
            + "<data name=\"unit\"><value>km</value></data>",
            "Strings.hr.resx");

        var problems = TranslationParity.Check(english, broken);

        Assert.Equal(
            new[]
            {
                TranslationProblemKind.BlankValue,
                TranslationProblemKind.NamedPlaceholderMismatch,
                TranslationProblemKind.NumericPlaceholderMismatch,
                TranslationProblemKind.IdenticalText,
            },
            problems.Select(problem => problem.Kind));
        Assert.Equal(
            BrokenKeys,
            problems.Select(problem => problem.Key));
        Assert.All(problems, problem => Assert.Equal("Strings.hr.resx", problem.File));
        Assert.All(problems, problem => Assert.NotNull(problem.Line));
    }
}
