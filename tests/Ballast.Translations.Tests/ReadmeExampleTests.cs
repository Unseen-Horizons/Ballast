// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.RegularExpressions;

using Ballast.Translations;

namespace Ballast.Translations.Tests;

/// <summary>
/// Runs the example in src/Ballast.Translations/README.md exactly as it is written there. The README's using lines are at the top
/// of this file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.Translations/README.md.
        // The English texts and a Croatian translation, as a flat JSON file holds them. An app reads its own files the same way.
        var english = JsonStringsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("""{"save":"Save","greeting":"Hello, {name}!"}""")), "en.json");
        var croatian = JsonStringsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("""{"save":"Spremi","greeting":"Bok, {name}!"}""")), "hr.json");

        // Every key, blank text and placeholder must agree. An empty list means the translation is in parity.
        var problems = TranslationParity.Check(english, croatian);
        Console.WriteLine(problems.Count);

        // Every key that a page asks for must be in the English texts.
        var pattern = new Regex("""__text\('(?<key>[a-z_]+)'\)""");
        var scan = LiteralKeyScan.Scan(english, pattern, [("index.html", "<p>__text('save')</p>\n<p>__text('cancel')</p>")]);
        Console.WriteLine(scan.Uses);
        Console.WriteLine(scan.Problems[0]);

        Assert.Empty(problems);
        Assert.Equal(2, scan.Uses);
        Assert.Equal("index.html:2: \"cancel\" is used here but is not in en.json.", scan.Problems[0].ToString());
    }
}
