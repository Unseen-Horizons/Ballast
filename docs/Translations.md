# Ballast.Translations: usage guide

Ballast.Translations checks that a translation holds the same texts as its base file. It reads two file formats, .resx
(the XML format .NET projects use) and flat JSON, and it compares a translation with the base file key by key. It also
checks that the keys source code asks for are in the base file. Every difference is returned as a
`TranslationProblem`, so a test asserts that the list is empty and prints each problem when it is not.

The package README has the install line and a runnable example. This guide covers every type and member, and every way each
one fails. The library depends on .NET alone (System.Xml.Linq, System.Text.Json and the regular expression engine), so it
runs in a server and in a test without any outside service.

## How the pieces fit

1. A reader turns a file into a `TranslationMap`: `ResxReader` for a .resx file, `JsonStringsReader` for a JSON object.
   A map holds its entries in file order and the problems the reader found (a duplicate key, for example).
2. `TranslationParity.Check` compares one or more translations with the base map and returns the problems.
3. `LiteralKeyScan.Scan` (or `ScanFiles`) finds the keys that source text asks for, and returns a problem for each key the
   base map does not hold.
4. The test asserts that each list is empty:

```csharp
Assert.Empty(TranslationParity.Check(english, croatian, options));
```

The library returns lists and does not assert. It does throw for a misuse of its own API (a null argument, a blank name),
and for a file that cannot be read as its format at all. A difference between two texts is never an exception.

## TranslationEntry

```csharp
public sealed record TranslationEntry(string Key, string Value, int? Line = null);
```

One key and its text, as a file gave them.

| Member | Meaning |
|---|---|
| `Key` | The key. Keys are compared exactly, letter case included. |
| `Value` | The text, as the file holds it. It is not trimmed, so the checks see the same text the file has. |
| `Line` | The line the entry starts on, when the file has lines (a .resx file does). Null for JSON and for entries built in code. |

## TranslationMap

```csharp
public sealed class TranslationMap
```

The texts of one file: its entries in file order, and the problems found while the file was read or the map was built.

### Constructor

```csharp
public TranslationMap(string name, IEnumerable<TranslationEntry> entries, IEnumerable<TranslationProblem>? readProblems = null)
```

Use it to read a format this library does not read, or to build a map in a test. It is what the two readers call.

| Parameter | Required | Meaning |
|---|---|---|
| `name` | yes | The name of the file. Every problem about the map names it. |
| `entries` | yes | The entries in file order. Each entry, and each entry's `Value`, must not be null. |
| `readProblems` | no | Problems found before the entries were given. They are kept as they are and come first in `Problems`. |

An entry whose key is null, empty or only white space is not kept. It is reported as `UnnamedEntry`. An entry whose key is
already in the map is not kept a second time: it is reported as `DuplicateKey`, and the first value stays. So the map holds
each key once, and a duplicate is never silently overwritten.

Ways it fails:

| Input | Result |
|---|---|
| `name` is null | `ArgumentNullException` |
| `name` is empty or only white space | `ArgumentException` |
| `entries` is null | `ArgumentNullException` |
| An entry is null, or an entry's `Value` is null | `ArgumentNullException` |
| An entry's key is blank | Not thrown. The entry is dropped and an `UnnamedEntry` problem is added. |
| A key is repeated | Not thrown. The later entry is dropped and a `DuplicateKey` problem is added. |

### Members

| Member | Type | Meaning |
|---|---|---|
| `Name` | `string` | The name of the file, as given. |
| `Entries` | `IReadOnlyList<TranslationEntry>` | The entries in file order. Each key appears once. |
| `Problems` | `IReadOnlyList<TranslationProblem>` | The problems found while the map was read or built, in the order found. Empty for a clean file. |
| `Count` | `int` | The number of keys. |
| `ContainsKey(string key)` | `bool` | Whether the key is in the map. The comparison is exact. |
| `TryGetValue(string key, out string value)` | `bool` | Whether the key is in the map. `value` is the text, or the empty string when the key is missing. |

`ContainsKey` and `TryGetValue` throw `ArgumentNullException` for a null key.

## ResxReader

```csharp
public static class ResxReader
```

Reads a .resx file. Only the `data` elements are read, in file order. A `data` element's `name` attribute is its key, and its
`value` child is its text. The `resheader` and `metadata` elements are not texts and are skipped. A `data` element with no
`value` child has an empty text, which the parity checks report as blank.

### ResxReader.ReadFile

```csharp
public static TranslationMap ReadFile(string path)
```

Reads the file at `path`. The map is named with the path as it was given, so a problem names the file the way the caller
wrote it.

| Input or situation | Result |
|---|---|
| `path` is null | `ArgumentNullException` |
| `path` is empty or only white space | `ArgumentException` |
| The file does not exist | `FileNotFoundException` |
| The folder does not exist | `DirectoryNotFoundException` |
| The file cannot be opened for reading (for example, the path names a folder) | The system's I/O or access exception propagates, such as `UnauthorizedAccessException` or `DirectoryNotFoundException` |
| The file is not well-formed XML | `InvalidDataException`, whose message names the file and whose inner exception is the `XmlException` |
| The root element is not `root` | `InvalidDataException`, which names the file |
| The file has a document type declaration (a DTD) | `InvalidDataException`, which names the file. A resx file has none, and a DTD could declare an entity that reads a local file, so it is refused. |

### ResxReader.Read

```csharp
public static TranslationMap Read(Stream stream, string name)
```

Reads a .resx document from a stream, to its end. The stream is left open. `name` is the name the map and its problems carry.

| Input or situation | Result |
|---|---|
| `stream` is null | `ArgumentNullException` |
| `name` is null | `ArgumentNullException` |
| `name` is empty or only white space | `ArgumentException` |
| The stream cannot be read (its `Read` throws) | `NotSupportedException`, as the stream raises it |
| The document is malformed, or its root is not `root`, or it has a DTD | `InvalidDataException`, as in `ReadFile` |

Problems the reader reports (in the map's `Problems`, not thrown):

- `DuplicateKey`: a key is repeated. The `Line` is the later element's line, and the message names the first line.
- `UnnamedEntry`: a `data` element has no `name`, or a blank one. It is not kept.

## JsonStringsReader

```csharp
public static class JsonStringsReader
```

Reads a JSON document whose root is an object. A member whose value is a string is a text. A member whose value is an object
is flattened: its members are read under the parent key joined to the member name with the separator. Other values are
reported and not read.

### JsonStringsReader.ReadFile

```csharp
public static TranslationMap ReadFile(string path, string separator = ".")
```

| Parameter | Default | Meaning |
|---|---|---|
| `path` | required | The file to read. The map is named with the path as given. |
| `separator` | `.` | The text that joins a parent key and a member name in a flattened key. It must not be blank. |

With the default separator, `{"dialog":{"close":"Close"}}` gives the key `dialog.close`. With `/`, the same document gives
`dialog/close`. A separator is written as given, so it may be more than one character.

| Input or situation | Result |
|---|---|
| `path` or `separator` is null | `ArgumentNullException` |
| `path` or `separator` is empty or only white space | `ArgumentException` |
| The file does not exist | `FileNotFoundException` |
| The folder does not exist | `DirectoryNotFoundException` |
| The file cannot be opened for reading | `UnauthorizedAccessException`, or the I/O exception the system gives |
| The file is not valid JSON, or its root is not an object | `InvalidDataException`, which names the file. Comments and trailing commas are not valid JSON here, and they are refused. |

### JsonStringsReader.Read

```csharp
public static TranslationMap Read(Stream stream, string name, string separator = ".")
```

Reads a JSON document from a stream, to its end. The stream is left open. A UTF-8 byte order mark at the start is accepted.

| Input or situation | Result |
|---|---|
| `stream` is null | `ArgumentNullException` |
| `name` or `separator` is null | `ArgumentNullException` |
| `name` or `separator` is empty or only white space | `ArgumentException` |
| The stream cannot be read (its `Read` throws) | `NotSupportedException`, as the stream raises it |
| The document is not valid JSON, or its root is not an object | `InvalidDataException`, which names `name`. The inner exception is the `JsonException` for invalid JSON. |

How the document is flattened:

- A string value is a text. Its key is the parent keys joined with the separator.
- An object value is flattened. An empty object adds nothing and is not reported, because it holds no text.
- Any other value (a number, `true`, `false`, `null` or an array) is not read. It is reported as `UnsupportedValue`, and the
  message says which kind of value it is.
- A member whose name is null, empty or only white space is not read. It is reported as `UnnamedEntry`. For a member inside
  an object, the problem's key is that object's key.
- A flattened key that is already in the map is reported as `DuplicateKey`. This covers a member repeated in one object
  (`{"save":"a","save":"b"}`) and a collision between a flattened key and a dotted member name
  (`{"a":{"b":"1"},"a.b":"2"}` both give `a.b`). The first value stays.

A flattened key is only the parent keys joined with the separator. If a member name itself holds the separator, nothing marks
it as a nested key, so a document with a single such member is read without a problem. Choose a separator that the keys do
not hold, or keep the keys flat.

## TranslationParity

```csharp
public static class TranslationParity
```

The parity checks. Each translation is compared with the base map, and each difference is a `TranslationProblem`. The checks
never throw for a difference.

### TranslationParity.Check (one translation)

```csharp
public static IReadOnlyList<TranslationProblem> Check(TranslationMap baseMap, TranslationMap translation, TranslationParityOptions? options = null)
```

### TranslationParity.Check (several translations)

```csharp
public static IReadOnlyList<TranslationProblem> Check(TranslationMap baseMap, IEnumerable<TranslationMap> translations, TranslationParityOptions? options = null)
```

The base map's own problems and blank texts are reported once. Each translation is then checked in the order given, and its
problems follow.

Order of the problems: the base map's reader problems, then the base map's blank texts (in file order). Then, for each
translation: its reader problems, its blank texts (in file order), the keys missing from it, its extra keys, then for each key
that both files have, in ordinal order, the identical-text problem, the named-placeholder problem and the numeric-placeholder
problem. Last come the stale allow-list entries. Missing, extra and per-key problems are in ordinal key order.

Ways it fails:

| Input | Result |
|---|---|
| `baseMap` is null | `ArgumentNullException` |
| `translation` is null, or `translations` is null, or an item in `translations` is null | `ArgumentNullException` |
| `options.SameTextAllowList` is null | `ArgumentException` |
| An allow-list entry is null, empty or only white space | `ArgumentException` |

Nothing else throws. A difference is a problem in the returned list. An empty `translations` list returns only the base map's
own problems.

### What each check reports

Every problem has the file, the key (when it is about one key), the line (when the file has one), the kind and a message.

| Kind | Reported when | Where it is reported |
|---|---|---|
| `DuplicateKey` | A key is repeated in a file. The first value is kept. | The file with the repeat. From a reader or from the map constructor. |
| `UnnamedEntry` | An entry has no name. It is not kept. | The file with the entry. |
| `UnsupportedValue` | A JSON value is not a string or an object. It is not read. | The JSON file. |
| `MissingFromTranslation` | A key is in the base map but not in the translation. | The translation. |
| `ExtraInTranslation` | A key is in the translation but not in the base map. | The translation, with the extra entry's line. |
| `BlankValue` | A text is empty or only white space. It is reported in the base map and in each translation. | The file with the blank text, with its line. |
| `NamedPlaceholderMismatch` | The named placeholders (`{name}`) of a key differ as sets. | The translation, with the translated entry's line. |
| `NumericPlaceholderMismatch` | The numeric placeholders (`{0}`, `{1:N2}`) of a key differ as sets. | The translation, with the translated entry's line. |
| `IdenticalText` | A key has the same text in both files, and is not on the same-text allow-list. A blank text is not reported here. | The translation, with the translated entry's line. |
| `StaleAllowListEntry` | An allow-list key is not in the base map, or not in the translation, or its two texts are no longer the same. | The translation. The entry must be removed or fixed. |
| `UnknownKey` | A scan found a key the base map does not hold. | The source file, with the line of the use. Only from `LiteralKeyScan`. |

Example messages, with the file names as given to the maps:

```
SharedResources.resx:12: "save" has a blank value in SharedResources.resx.
hr.json: "save" is in en.json but not in hr.json.
hr.json: "greeting" has named placeholders {name} in en.json but {ime} in hr.json.
hr.json: "items" has numeric placeholders {0} in en.json but {0} {1} in hr.json.
hr.json: "unit" has the same text in en.json and hr.json ("km"): translate it, or add the key to the same-text allow-list with its reason.
hr.json: "gone" is on the same-text allow-list but is not in en.json: remove it.
```

### Placeholders

Placeholders are compared as sets, so the order of the placeholders and how often each appears do not matter.

- A **named** placeholder is `{` followed by a name that starts with a letter or an underscore, then `}`. A format or an
  alignment after the name is part of the placeholder, but not of the name: `{due:d MMM}` and `{due}` are both `due`.
- A **numeric** placeholder is `{` followed by ASCII digits, then `}`, with the same optional format or alignment. `{0:d MMM}`
  and `{0}` are both `0`, and `{01}` is the same as `{1}`.
- A named placeholder and a numeric one never match each other.
- Doubled braces (`{{` and `}}`) are not read as escapes. A text that holds `{{0}}` counts as having `{0}`. The texts checked
  so far (the usage examples and the resx files of the two first apps that use the library) hold no doubled braces. A caller
  whose texts do need a literal brace must not rely on this check for those keys.

The check does not look at which numbers appear. A text with only `{1}` has the same set as one with `{1}` in the base, so the
parity check passes, even though .NET would need an argument for `{0}` too. Checking that a call passes enough arguments is the
caller's job (see "What the library does not check").

### Same-text allow-list

A key whose text is the same in both files is a problem, unless it is on the allow-list. Use the list for a word that a
language writes the same way, such as a unit, a code, or a language's name written in itself. The list holds keys, not
reasons: keep the reason in a comment beside each key in the app.

An entry is stale when its key is not in the base map, not in the translation, or its two texts differ. A stale entry is
reported so that the list does not keep allowing a key that no longer needs it.

A blank text is never reported as identical, because the blank check already reports it. Two blank texts for one key give
two `BlankValue` problems, not an identical-text problem too.

## TranslationParityOptions

```csharp
public sealed record TranslationParityOptions
```

| Option | Type | Default | Meaning |
|---|---|---|---|
| `SameTextAllowList` | `IEnumerable<string>` | empty | The keys whose text may be the same in both files, on purpose. See "Same-text allow-list". Each entry is compared exactly. |

The defaults check everything. There is no option to switch a check off. An app that does not want one kind of problem filters
the list by kind, as shown in "Using it in a test".

## LiteralKeyScan

```csharp
public static class LiteralKeyScan
```

Finds the literal keys that source text asks for, and reports each one the base map does not hold. The caller gives the
pattern, because only the caller knows how its code asks for a text.

### How the pattern works

The pattern is a `System.Text.RegularExpressions.Regex` with a named group that captures the key. The default group name is
`key`. Each match that captures the key is one use of that key. A match in which the group did not take part is skipped and is
not a use.

Several ways of asking for a text can be one pattern. Name the same group in each alternative, and the engine captures
whichever alternative matched:

```csharp
var pattern = new Regex("""data-i18n="(?<key>[a-z_]+)"|__text\('(?<key>[a-z_]+)'\)""");
```

The scan does not understand the source language. A match inside a comment or a string literal counts as a use. Write the
pattern so that it matches only what the code really asks for.

### LiteralKeyScan.Scan

```csharp
public static LiteralKeyScanResult Scan(TranslationMap baseMap, Regex pattern, IEnumerable<(string File, string Text)> sources, string keyGroup = "key")
```

| Parameter | Default | Meaning |
|---|---|---|
| `baseMap` | required | The map every key must be in. |
| `pattern` | required | The pattern. It must have a group named `keyGroup`. |
| `sources` | required | The source texts, each with the name it is reported under. |
| `keyGroup` | `key` | The name of the group that captures the key. |

Each problem carries the source's name, the key, the line of the use (counted from 1, by the line breaks before the key), the
kind `UnknownKey` and a message: `"deleted" is used here but is not in en.json.`

Ways it fails:

| Input | Result |
|---|---|
| `baseMap`, `pattern`, `sources`, or a source's text is null | `ArgumentNullException` |
| `keyGroup` is null | `ArgumentNullException` |
| `keyGroup` is empty or only white space | `ArgumentException` |
| The pattern has no group named `keyGroup` | `ArgumentException`, whose parameter name is `pattern` |

### LiteralKeyScan.ScanFiles

```csharp
public static LiteralKeyScanResult ScanFiles(TranslationMap baseMap, Regex pattern, IEnumerable<string> paths, string keyGroup = "key")
```

Reads each file as text when the scan reaches it, and scans it. The file is read with its byte order mark detected, and UTF-8
when there is none. It is reported under the path it was given. The caller chooses the files: the library does not walk a
folder or skip files by name.

Ways it fails: the same as `Scan`, plus:

| Input or situation | Result |
|---|---|
| `paths` is null, or a path is null | `ArgumentNullException` |
| A file does not exist | `FileNotFoundException` |
| A folder does not exist | `DirectoryNotFoundException` |
| A file cannot be read | `UnauthorizedAccessException`, or the I/O exception the system gives |

### LiteralKeyScanResult

```csharp
public sealed record LiteralKeyScanResult(int Uses, IReadOnlyList<TranslationProblem> Problems);
```

| Member | Meaning |
|---|---|
| `Uses` | The number of key uses found, whether or not the key is in the base map. A test that expects a minimum asserts on this, so a pattern that finds nothing cannot pass. |
| `Problems` | One problem per use whose key is not in the base map, in the order the sources were scanned. |

A scan that finds nothing returns `Uses` of 0 and no problems, so a test that requires uses checks `Uses` itself.

## TranslationProblem

```csharp
public sealed record TranslationProblem(string File, string? Key, int? Line, TranslationProblemKind Kind, string Message)
```

| Member | Meaning |
|---|---|
| `File` | The file the problem is in: the name a map was given, or the source file of a scan. |
| `Key` | The key the problem is about, or null when it is not about one key. |
| `Line` | The line in the file, or null when the file has no lines or the problem is not on one line. |
| `Kind` | The kind, from `TranslationProblemKind`. |
| `Message` | A sentence that says what is wrong. |

`ToString` gives the problem on one line: `file:line: message`, or `file: message` without a line. Test output shows this text,
so `Assert.Empty` prints every problem in a form a person can act on.

## TranslationProblemKind

```csharp
public enum TranslationProblemKind
```

The names are part of the contract. A kind is only ever added at the end of the enum. The kinds are listed with their checks in
"What each check reports".

| Kind | Meaning |
|---|---|
| `DuplicateKey` | A key is repeated in one file. |
| `UnnamedEntry` | An entry has no name. |
| `UnsupportedValue` | A JSON value is not a string. |
| `MissingFromTranslation` | A base key is not in the translation. |
| `ExtraInTranslation` | A translation key is not in the base. |
| `BlankValue` | A text is empty or only white space. |
| `NamedPlaceholderMismatch` | The named placeholders of a key differ. |
| `NumericPlaceholderMismatch` | The numeric placeholders of a key differ. |
| `IdenticalText` | A text is the same in both files and is not on the allow-list. |
| `StaleAllowListEntry` | An allow-list entry no longer applies. |
| `UnknownKey` | A scanned key is not in the base map. |

## Using it in a test

The shape of a test that checks a pair of resx files:

```csharp
var english = ResxReader.ReadFile(englishPath);
var croatian = ResxReader.ReadFile(croatianPath);
var options = new TranslationParityOptions { SameTextAllowList = ["unit"] };

Assert.Empty(TranslationParity.Check(english, croatian, options));
```

When the list is not empty, xUnit prints each problem through `ToString`. A test that checks only some kinds filters the list
first, for example `problems.Where(p => p.Kind is TranslationProblemKind.MissingFromTranslation or TranslationProblemKind.ExtraInTranslation)`.

## Generating a site's translation files at build time

A site that keeps its texts in .resx files (the base file is English, and each other culture is `Base.culture.resx`) needs the
JSON files and the script that its pages load. `I18nGenerator.Generate` writes them, and the package's build task runs it
during the build, in place of a Node script.

What it writes, for the base name `SharedResources`:

- `<OutputDirectory>/<culture>.json` for each culture, as `JSON.stringify(texts, null, 2)` writes it, with no line break at the end.
  English is written first in the culture list; the others follow in ordinal order.
- `<DataFile>`, the script, with four lines after its header: `window.__I18N_DATA` (the English texts),
  `window.__STATUS_MAP` (each `status_*` key by its code in Pascal case: `status_not_found` gives `NotFound`),
  `window.__I18N_LANGS` (the culture list) and `window.ERROR_CODE_MAP` (the optional error-code file).

The output is written as UTF-8 without a byte order mark, with line feeds. The generator's tests compare its output
byte for byte with the output of the Node script it replaces, for a real site's .resx files.

### The options

| Option | Default | Meaning |
|---|---|---|
| `SourceDirectory` | required | The folder with the .resx files. Only that folder is read, not its subfolders. |
| `BaseName` | `SharedResources` | The base name. The base file is `BaseName.resx`; a culture file is `BaseName.culture.resx`, where the culture is letters, digits and underscores. |
| `OutputDirectory` | required | The folder of the `culture.json` files. Created when missing. |
| `DataFile` | required | The script to write. Its folder is created when missing. Its file name is what the loader check looks for. |
| `ErrorCodesFile` | null | A JSON object of error code to key. Null writes `{}`. A named file must exist, and every value must be text. |
| `WebRoot` | null | The folder whose HTML files are checked. Null turns the check off. The folders named `i18n` are not searched. |
| `LoaderScript` | null | The loader the HTML files load, such as `/js/i18n.js`. Every HTML file that loads it must load the data script before it. |
| `Header` | `// Generated by Ballast.Translations — do not edit manually` | The script's first line, without its line break. A site that must keep its earlier header sets it here. |

In the MSBuild target (`build/Ballast.Translations.targets`, imported by the package) the same options are the properties
`BallastI18nBaseName` (required to turn the target on), `BallastI18nSourceDirectory` (the project folder by default),
`BallastI18nOutputDirectory` (`wwwroot\i18n`), `BallastI18nDataFile` (`wwwroot\js\i18n-data.js`), `BallastI18nErrorCodesFile`,
`BallastI18nWebRoot`, `BallastI18nLoaderScript` and `BallastI18nHeader`. A project that does not set `BallastI18nBaseName` is not changed.

```xml
<PropertyGroup>
  <BallastI18nBaseName>SharedResources</BallastI18nBaseName>
  <BallastI18nErrorCodesFile>$(MSBuildProjectDirectory)\build\api-error-codes.json</BallastI18nErrorCodesFile>
  <BallastI18nWebRoot>$(MSBuildProjectDirectory)\wwwroot</BallastI18nWebRoot>
  <BallastI18nLoaderScript>/js/i18n.js</BallastI18nLoaderScript>
</PropertyGroup>
```

### What it refuses, and what it notices

The generator writes nothing when it refuses. A refusal fails the build with every problem listed:

- a .resx file that is not well-formed, or whose root is not `root`, or that holds a DTD;
- a key that is in a file twice (the script would have to pick one; the reader keeps the first value, which the generator does not);
- two files for one culture (for example `SharedResources.resx` and `SharedResources.en.resx`);
- two `status_*` keys that give one code (`status_not_found` and `status_NOT_FOUND`);
- an error-code value that is not text, a key in the error-code file given twice, or text with an unpaired surrogate escape;
- a named error-code file that does not exist, or a source folder that does not exist;
- a blank option (the source folder, the output folder, the data file or the header).

The generator notices, and does not stop for: an entry with no name. That entry is left out, and the build logs it as a warning.

After the files are written, the loader check runs. An HTML file that loads the loader script without loading the data script
first is listed, and the build fails. The files are written before the check, as the script wrote them, so a listed page
still has its data.

### Where it differs from the script

- A key in a file twice is an error. The script kept the last value.
- Two files for one culture are an error. The script wrote the culture twice.
- A status key collision is an error. The script kept the last key.
- The culture list is English first, then the other cultures in ordinal order. The script used the order the file system listed them in.
- An error-code file that is named but missing is an error. The script wrote `{}`.
- An error-code value that is not text is an error. The script would have written it as JSON, which this generator does not handle.
- The .resx values are read with the XML parser. The script read them with a pattern and decoded four entities. For a file
  where the two give the same text (every real site file tested, with `&amp;`, `&lt;` and `&gt;`), the output is the same.
  A file with a CR inside a value, or an entity the script decoded but the parser does not, could differ.
- The trim is JavaScript's: the same white space set as `trim()`, not .NET's.

## What the library does not check

These checks stay in the app, because they need knowledge the library does not have:

- **Argument counts at call sites.** A call such as `localizer["key", a, b]` needs the text to use placeholders up to `{1}`.
  Reading C# to find the call and its arguments is the app's job. The library compares the placeholders of two texts, not
  the arguments of a call.
- **Contiguous numeric indexes.** A text with only `{1}` passes the parity check, because both files have the same set. The
  app checks that the indexes start at 0 and have no gaps, when it needs that.
- **Which files to scan.** The caller lists the files, and skips the ones that are generated, vendored or not source.
- **Keys that are not literals.** A key built at run time (`"status_" + name`) is invisible to the scan. The pattern cannot
  see it, and the library does not guess.
- **The meaning of a text.** The checks compare keys, texts and placeholders. They do not check that a translation is good.

## Setup

None. The library needs no key, account or outside service.
