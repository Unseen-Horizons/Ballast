// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations.Tests;

public class TranslationParityTests
{
    private static readonly TranslationParityOptions AllowUnit = new() { SameTextAllowList = ["unit"] };
    private static readonly string[] EnglishAndCroatian = ["en.json", "hr.json"];
    private static readonly string[] EnglishCroatianGerman = ["en.json", "hr.json", "de.json"];

    private static TranslationMap English() => TestData.Map("en.json",
        ("save", "Save"),
        ("greeting", "Hello, {name}!"),
        ("items", "{0} items"),
        ("price", "Price {0:N2} EUR"),
        ("unit", "km"));

    private static TranslationMap Croatian() => TestData.Map("hr.json",
        ("save", "Spremi"),
        ("greeting", "Bok, {name}!"),
        ("items", "{0} stavki"),
        ("price", "Cijena {0:N2} EUR"),
        ("unit", "km"));

    [Fact]
    public void A_translation_in_parity_has_no_problems()
    {
        Assert.Empty(TranslationParity.Check(English(), Croatian(), AllowUnit));
    }

    [Fact]
    public void A_key_missing_from_the_translation_is_reported()
    {
        var hr = TestData.Map("hr.json",
            ("greeting", "Bok, {name}!"), ("items", "{0} stavki"), ("price", "Cijena {0:N2} EUR"), ("unit", "km"));

        var problem = Assert.Single(TranslationParity.Check(English(), hr, AllowUnit));

        Assert.Equal(TranslationProblemKind.MissingFromTranslation, problem.Kind);
        Assert.Equal("save", problem.Key);
        Assert.Equal("hr.json", problem.File);
        Assert.Null(problem.Line);
        Assert.Equal("\"save\" is in en.json but not in hr.json.", problem.Message);
    }

    [Fact]
    public void An_extra_key_in_the_translation_is_reported_with_its_line()
    {
        var hr = TestData.Resx(
            "<data name=\"save\"><value>Spremi</value></data>\n"
            + "<data name=\"extra\"><value>Dodatno</value></data>\n"
            + "<data name=\"greeting\"><value>Bok, {name}!</value></data>\n"
            + "<data name=\"items\"><value>{0} stavki</value></data>\n"
            + "<data name=\"price\"><value>Cijena {0:N2} EUR</value></data>\n"
            + "<data name=\"unit\"><value>km</value></data>",
            "hr.resx");

        var problem = Assert.Single(TranslationParity.Check(English(), hr, AllowUnit));

        Assert.Equal(TranslationProblemKind.ExtraInTranslation, problem.Kind);
        Assert.Equal("extra", problem.Key);
        Assert.Equal("hr.resx", problem.File);
        Assert.Equal(hr.Entries.Single(entry => entry.Key == "extra").Line, problem.Line);
        Assert.Equal("\"extra\" is in hr.resx but not in en.json.", problem.Message);
    }

    [Fact]
    public void A_blank_text_is_reported_in_the_base_file_and_in_the_translation()
    {
        var en = TestData.Map("en.json", ("save", string.Empty), ("unit", "km"));
        var hr = TestData.Map("hr.json", ("save", "   "), ("unit", "km"));

        var problems = TranslationParity.Check(en, hr, AllowUnit);

        Assert.Equal(new[] { TranslationProblemKind.BlankValue, TranslationProblemKind.BlankValue }, problems.Select(p => p.Kind));
        Assert.Equal(EnglishAndCroatian, problems.Select(p => p.File));
        Assert.Equal("\"save\" has a blank value in en.json.", problems[0].Message);
    }

    [Fact]
    public void A_blank_text_that_matches_a_blank_base_text_is_not_also_reported_as_identical()
    {
        var en = TestData.Map("en.json", ("save", string.Empty));
        var hr = TestData.Map("hr.json", ("save", string.Empty));

        var problems = TranslationParity.Check(en, hr);

        Assert.DoesNotContain(problems, problem => problem.Kind == TranslationProblemKind.IdenticalText);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void A_named_placeholder_that_differs_is_reported()
    {
        var hr = TestData.Map("hr.json",
            ("save", "Spremi"), ("greeting", "Bok, {ime}!"), ("items", "{0} stavki"), ("price", "Cijena {0:N2} EUR"), ("unit", "km"));

        var problem = Assert.Single(TranslationParity.Check(English(), hr, AllowUnit));

        Assert.Equal(TranslationProblemKind.NamedPlaceholderMismatch, problem.Kind);
        Assert.Equal("greeting", problem.Key);
        Assert.Equal("\"greeting\" has named placeholders {name} in en.json but {ime} in hr.json.", problem.Message);
    }

    [Fact]
    public void A_named_placeholder_with_a_format_is_the_same_placeholder_as_one_without()
    {
        var en = TestData.Map("en.json", ("due", "Due {due:d MMM}"));
        var hr = TestData.Map("hr.json", ("due", "Rok {due}"));

        Assert.Empty(TranslationParity.Check(en, hr));
    }

    [Fact]
    public void A_numeric_placeholder_that_differs_is_reported()
    {
        var hr = TestData.Map("hr.json",
            ("save", "Spremi"), ("greeting", "Bok, {name}!"), ("items", "{0} stavki {1}"), ("price", "Cijena {0:N2} EUR"), ("unit", "km"));

        var problem = Assert.Single(TranslationParity.Check(English(), hr, AllowUnit));

        Assert.Equal(TranslationProblemKind.NumericPlaceholderMismatch, problem.Kind);
        Assert.Equal("items", problem.Key);
        Assert.Equal("\"items\" has numeric placeholders {0} in en.json but {0} {1} in hr.json.", problem.Message);
    }

    [Fact]
    public void Numeric_placeholders_are_compared_as_sets_so_format_order_and_leading_zeros_do_not_matter()
    {
        var en = TestData.Map("en.json", ("range", "{0:d MMM} to {1}"), ("code", "Code {01}"));
        var hr = TestData.Map("hr.json", ("range", "{1} do {0:d. MMM}"), ("code", "Šifra {1,5}"));

        Assert.Empty(TranslationParity.Check(en, hr));
    }

    [Fact]
    public void Identical_text_is_reported_unless_the_key_is_on_the_allow_list()
    {
        var problem = Assert.Single(TranslationParity.Check(English(), Croatian()));

        Assert.Equal(TranslationProblemKind.IdenticalText, problem.Kind);
        Assert.Equal("unit", problem.Key);
        Assert.Equal(
            "\"unit\" has the same text in en.json and hr.json (\"km\"): translate it, or add the key to the same-text allow-list with its reason.",
            problem.Message);
        Assert.Empty(TranslationParity.Check(English(), Croatian(), AllowUnit));
    }

    [Fact]
    public void An_allow_list_entry_whose_key_is_not_in_the_base_file_is_stale()
    {
        var options = new TranslationParityOptions { SameTextAllowList = ["unit", "gone"] };

        var problem = Assert.Single(TranslationParity.Check(English(), Croatian(), options));

        Assert.Equal(TranslationProblemKind.StaleAllowListEntry, problem.Kind);
        Assert.Equal("gone", problem.Key);
        Assert.Equal("hr.json", problem.File);
        Assert.Equal("\"gone\" is on the same-text allow-list but is not in en.json: remove it.", problem.Message);
    }

    [Fact]
    public void An_allow_list_entry_whose_key_is_missing_from_the_translation_is_stale()
    {
        var hr = TestData.Map("hr.json",
            ("save", "Spremi"), ("greeting", "Bok, {name}!"), ("items", "{0} stavki"), ("price", "Cijena {0:N2} EUR"));

        var problems = TranslationParity.Check(English(), hr, AllowUnit);

        Assert.Equal(new[] { TranslationProblemKind.MissingFromTranslation, TranslationProblemKind.StaleAllowListEntry }, problems.Select(p => p.Kind));
        Assert.Equal("\"unit\" is on the same-text allow-list but is not in hr.json: remove it.", problems[1].Message);
    }

    [Fact]
    public void An_allow_list_entry_whose_texts_no_longer_match_is_stale()
    {
        var hr = TestData.Map("hr.json",
            ("save", "Spremi"), ("greeting", "Bok, {name}!"), ("items", "{0} stavki"), ("price", "Cijena {0:N2} EUR"), ("unit", "km."));

        var problem = Assert.Single(TranslationParity.Check(English(), hr, AllowUnit));

        Assert.Equal(TranslationProblemKind.StaleAllowListEntry, problem.Kind);
        Assert.Equal("unit", problem.Key);
        Assert.Equal(
            "\"unit\" is on the same-text allow-list but its texts differ (en.json \"km\", hr.json \"km.\"): remove it from the allow-list.",
            problem.Message);
    }

    [Fact]
    public void A_blank_or_missing_allow_list_is_refused()
    {
        var blank = new TranslationParityOptions { SameTextAllowList = ["unit", " "] };
        var missing = new TranslationParityOptions { SameTextAllowList = null! };

        Assert.Throws<ArgumentException>(() => TranslationParity.Check(English(), Croatian(), blank));
        Assert.Throws<ArgumentException>(() => TranslationParity.Check(English(), Croatian(), missing));
    }

    [Fact]
    public void Several_translations_are_each_checked_and_the_base_problems_are_reported_once()
    {
        var en = TestData.Map("en.json", ("save", string.Empty), ("unit", "km"));
        var hr = TestData.Map("hr.json", ("unit", "km"));
        var de = TestData.Map("de.json", ("save", "Speichern"), ("unit", "km"), ("extra", "x"));

        var problems = TranslationParity.Check(en, [hr, de], AllowUnit);

        Assert.Equal(
            new[] { TranslationProblemKind.BlankValue, TranslationProblemKind.MissingFromTranslation, TranslationProblemKind.ExtraInTranslation },
            problems.Select(p => p.Kind));
        Assert.Equal(EnglishCroatianGerman, problems.Select(p => p.File));
    }

    [Fact]
    public void Problems_the_readers_found_are_part_of_the_check()
    {
        var hr = TestData.Resx(
            "<data name=\"save\"><value>Spremi</value></data>\n"
            + "<data name=\"save\"><value>Spremi</value></data>\n"
            + "<data name=\"greeting\"><value>Bok, {name}!</value></data>\n"
            + "<data name=\"items\"><value>{0} stavki</value></data>\n"
            + "<data name=\"price\"><value>Cijena {0:N2} EUR</value></data>\n"
            + "<data name=\"unit\"><value>km</value></data>",
            "hr.resx");

        var problem = Assert.Single(TranslationParity.Check(English(), hr, AllowUnit));

        Assert.Equal(TranslationProblemKind.DuplicateKey, problem.Kind);
        Assert.Equal("hr.resx", problem.File);
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => TranslationParity.Check(null!, Croatian()));
        Assert.Throws<ArgumentNullException>(() => TranslationParity.Check(English(), (TranslationMap)null!));
        Assert.Throws<ArgumentNullException>(() => TranslationParity.Check(English(), [null!]));
        Assert.Throws<ArgumentNullException>(() => TranslationParity.Check(English(), (IEnumerable<TranslationMap>)null!));
    }

    [Fact]
    public void A_problem_prints_its_file_its_line_and_its_message()
    {
        var withLine = new TranslationProblem("SharedResources.resx", "save", 12, TranslationProblemKind.BlankValue,
            "\"save\" has a blank value in SharedResources.resx.");
        var withoutLine = withLine with { Line = null };

        Assert.Equal("SharedResources.resx:12: \"save\" has a blank value in SharedResources.resx.", withLine.ToString());
        Assert.Equal("SharedResources.resx: \"save\" has a blank value in SharedResources.resx.", withoutLine.ToString());
    }
}
