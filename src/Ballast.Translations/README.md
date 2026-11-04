# Ballast.Translations

Checks that a translation holds the same texts as its base file. It reads .resx and flat JSON files, and reports each
difference as a problem that a test can print: a key missing from one file, a blank text, placeholders that differ, a text
that is still the same as the base, an allow-list entry that is no longer needed, and a key that source code asks for but the
base file does not hold. It depends on .NET alone, and the test that uses it is a plain `Assert.Empty`.

It can also generate a site's translation files at build time: a JSON file for each culture and the script that the pages load,
from the site's .resx files. The package's build task does it when a project sets `BallastI18nBaseName`. The guide has the
options, and what the generator refuses.

Install:

```
dotnet add package Ballast.Translations --prerelease
```

## Example

```csharp
using System.Text;
using System.Text.RegularExpressions;

using Ballast.Translations;

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
```

The example prints `0`, then `2`, then `index.html:2: "cancel" is used here but is not in en.json.`

The usage guide, with every option and every way the library fails, is `docs/Translations.md` in the repository.
