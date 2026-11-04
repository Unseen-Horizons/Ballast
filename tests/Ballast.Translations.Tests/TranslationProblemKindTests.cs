// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Translations.Tests;

/// <summary>
/// Runs every problem kind through a check that reports it. The examples are built here, so a kind added to the enum fails the
/// theory until an example for it is written. Each kind also has its own focused test in the other files.
/// </summary>
public class TranslationProblemKindTests
{
    public static TheoryData<TranslationProblemKind> EveryKind()
    {
        var kinds = new TheoryData<TranslationProblemKind>();
        foreach (var kind in Enum.GetValues<TranslationProblemKind>())
        {
            kinds.Add(kind);
        }

        return kinds;
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Each_kind_is_reported_by_the_check_that_owns_it(TranslationProblemKind kind)
    {
        var problems = Example(kind);

        Assert.Contains(problems, problem => problem.Kind == kind);
    }

    private static IReadOnlyList<TranslationProblem> Example(TranslationProblemKind kind) => kind switch
    {
        TranslationProblemKind.DuplicateKey => new TranslationMap("en.json", [new TranslationEntry("a", "1"), new TranslationEntry("a", "2")]).Problems,
        TranslationProblemKind.UnnamedEntry => new TranslationMap("en.json", [new TranslationEntry(string.Empty, "1")]).Problems,
        TranslationProblemKind.UnsupportedValue => TestData.Json("{\"n\":1}").Problems,
        TranslationProblemKind.MissingFromTranslation => TranslationParity.Check(TestData.Map("en.json", ("a", "x")), TestData.Map("hr.json")),
        TranslationProblemKind.ExtraInTranslation => TranslationParity.Check(TestData.Map("en.json"), TestData.Map("hr.json", ("a", "x"))),
        TranslationProblemKind.BlankValue => TranslationParity.Check(TestData.Map("en.json", ("a", " ")), TestData.Map("hr.json", ("a", "x"))),
        TranslationProblemKind.NamedPlaceholderMismatch => TranslationParity.Check(TestData.Map("en.json", ("a", "{x}")), TestData.Map("hr.json", ("a", "{y}"))),
        TranslationProblemKind.NumericPlaceholderMismatch => TranslationParity.Check(TestData.Map("en.json", ("a", "{0}")), TestData.Map("hr.json", ("a", "{1}"))),
        TranslationProblemKind.IdenticalText => TranslationParity.Check(TestData.Map("en.json", ("a", "x")), TestData.Map("hr.json", ("a", "x"))),
        TranslationProblemKind.StaleAllowListEntry => TranslationParity.Check(
            TestData.Map("en.json", ("a", "x")),
            TestData.Map("hr.json", ("a", "y")),
            new TranslationParityOptions { SameTextAllowList = ["a"] }),
        TranslationProblemKind.UnknownKey => LiteralKeyScan.Scan(
            TestData.Map("en.json", ("a", "x")),
            new Regex("(?<key>b)"),
            [("page.js", "b")]).Problems,
        _ => throw new InvalidOperationException($"No example for {kind}. Add one, with the check that reports it."),
    };
}
