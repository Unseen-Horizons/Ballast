# Ballast.Text: usage guide

Ballast.Text has two parts:

- `TextFold.Fold` folds text for comparison. Two spellings of one name fold to the same text.
- `PersonalDataMasker` hides email addresses, phone numbers and links in a text before the text goes somewhere it should not,
  such as a search index, a log or a model. `ContainsPersonalData` says whether there is anything to hide.

Install with `dotnet add package Ballast.Text --prerelease`. The package targets `net10.0` and depends on no package.

The examples show the library's actual output.

## TextFold

### `DStroke`

How the letter đ (and its capital Đ) folds. It has no accent to drop, so a rule must choose a letter for it.

| Member | Value | Effect |
|---|---|---|
| `D` | the default | đ folds to `d`: "Đakovo" becomes "dakovo" |
| `Dj` | | đ folds to `dj`: "Đakovo" becomes "djakovo", as some sources write such names |

Use `Dj` when you match names that an outside source writes with `dj`. Use `D` for everything else. A value folded with one
rule does not match a value folded with the other, so a stored folded value must be made with the rule it is compared under.

### `TextFold.Fold(string? text, DStroke dStroke = DStroke.D)`

Returns `text` folded for comparison.

The steps, in order:

1. Each letter is decomposed, so an accent becomes a separate combining mark, and the combining marks are dropped.
2. Apostrophes are deleted: `'`, `’`, `‘`, `ʼ` and the backtick. "McDonald's" becomes "mcdonalds".
3. The result is lower-cased in the invariant culture. `đ` becomes `d` or `dj` as `dStroke` says. `ß` becomes `ss`.
4. Whitespace and punctuation separate words. A run of them becomes one space, and there is no space at either end.

| `text` | Result |
|---|---|
| `"Trg bana Jelačića"` | `trg bana jelacica` |
| `"trg bana jelacica"` | `trg bana jelacica`: the same as the line above |
| `"Straße"` | `strasse` |
| `"Đakovo"` | `dakovo` |
| `"Đakovo"` with `DStroke.Dj` | `djakovo` |
| `"đurđevac"` with `DStroke.Dj` | `djurdjevac` |
| `"Zadar (gaženica)"` | `zadar gazenica` |
| `"Zadar -- Centar!!"` | `zadar centar` |
| `"Trg bana J. Jelačića"` | `trg bana j jelacica`: an abbreviation point is not expanded |
| `"Kod Mate’s"` | `kod mates` |
| `"a-b_c"` | `a b c`: a hyphen and an underscore are punctuation |
| `"  ZADAR  "` | `zadar` |
| `null`, `""`, `"   "`, `"!!! ... "` | `""` (the empty string) |

Folding is idempotent: folding a folded text changes nothing.

**What it does not do.**

- It is not a transliteration. Cyrillic stays Cyrillic: "Београд" folds to "београд".
- Symbols are not separators. "Zadar € centar" folds to "zadar € centar", and "Ana+Ivan" to "ana+ivan". A dot is
  punctuation, so "1.59 €" folds to "1 59 €".
- Only đ and ß (and their capitals) have a rule of their own. Every other letter keeps its own letter, once its accent is dropped.
- Letters outside the Basic Multilingual Plane are not lower-cased.
- Hangul syllables are decomposed into their letters by the normalisation that runs first.
- The result is for comparing and must not be shown to a person.

**Fails.**

- `ArgumentOutOfRangeException`, with `ParamName` `"dStroke"`, when `dStroke` is not one of the two defined values. For
  example, `(DStroke)7`.
- `ArgumentException`, when `text` contains an unpaired surrogate (a half of a surrogate pair with no partner). This is not
  text. It is refused rather than replaced, so the caller can report the bad input. The exception comes from .NET's own
  normalisation, which rejects every such string on this platform.
- Never a `null` result. Null or empty text gives the empty string, which is a legitimate fold (nothing to compare).

## PersonalDataMasker

### `PersonalDataMasker.Mask(string? text)`

Returns `text` with its links, emails and phone numbers replaced by placeholders. The rules run in this order:

1. **Links.** `http://`, `https://` and `www.` links. A link keeps only its host, without `www.`, in the form `[link: host]`.
   Punctuation that ends a sentence after the link is kept after the placeholder.
2. **Emails**, in the form `[email]`. The match is case-insensitive.
3. **Phone numbers**, in the form `[phone]`. Two forms match:
   - a run of seven or more digits with no separator, with an optional leading `+`;
   - a number with separators, which starts with `+`, `0` or `(0`, has eight or more digits in all, and whose separators are
     only a space, `-`, `/` or brackets. A dot is never a phone separator.

   The match must not touch a letter, a digit or a colon on either side.
4. **Dates are kept.** A phone match that has the form of a date (`2026-10-26`, `26.10.2026` or `01-10-2026`) is left alone.

| Input | Output |
|---|---|
| `"Napišite na ana.kovac+news@example.co.uk ili +385 91 234 5678."` | `Napišite na [email] ili [phone].` |
| `"https://zet.hr"` | `[link: zet.hr]` |
| `"Vidi www.zet.hr."` | `Vidi [link: zet.hr].` |
| `"https://zet.hr/x?email=ana@example.com"` | `[link: zet.hr]`: the link is masked whole, its query with it |
| `"https://localhost:5000/x"` | `[link: localhost]`: the port is dropped with the rest of the path |
| `"http://10.0.2.2:7230/"` | `[link: 10.0.2.2]` |
| `"ANA@EXAMPLE.COM"` | `[email]` |
| `"091/123-45-67"` | `[phone]` |
| `"+385 (0)91 123 4567"` | `[phone]` |
| `"+1 555 0100"` | `[phone]` |
| `"Nazovite 091 123 4567 sutra"` | `Nazovite [phone] sutra` |
| `"Od 2026-10-26 do 26.10.2026."` | `Od 2026-10-26 do 26.10.2026.` (dates kept) |
| `"26.10.2026 14:05"` | `26.10.2026 14:05` (a date and a time kept) |
| `"autobus u 14.05 i 15.10 nije došao"` | unchanged (times kept) |
| `"linije 101 102 103"` | unchanged (a list of line numbers kept) |
| `"12,50 €"` | unchanged (a price kept) |
| `"123456"` | unchanged: six digits is not a phone number |
| `"Čekam a@b.hr i 0911234567!"` | `Čekam [email] i [phone]!` |
| `null`, `""` | `""` (the empty string) |

**Letters are never changed.** Text in any script passes through unchanged: Croatian, Serbian Cyrillic and Hungarian are
kept as they were typed.

**What it misses.** The masker finds only the patterns above. These pass through unchanged:

| Input | Why it is not masked |
|---|---|
| `"Nazovi 123 4567"` | A separated number must start with `+`, `0` or `(0` |
| `"zet.hr/vozni-red"` | A link needs `http://`, `https://` or `www.` |
| `"ftp://zet.hr/file"` | Only `http` and `https` schemes are links |
| `"ana [at] example dot com"` | An address written in words is not an email |
| `"ana@example"` | An email needs a dot and a top-level domain of two or more letters |

**What it hides by mistake.** A run of seven or more digits is a phone number, even when it is an identifier:

| Input | Output |
|---|---|
| `"Nalog 1234567"` | `Nalog [phone]` |

**Fails.** Never throws. A value that does not match is returned unchanged. A value that is hidden is replaced by one of the
placeholders above.

### `PersonalDataMasker.ContainsPersonalData(string? text)`

Returns `true` when `Mask(text)` would change `text`, and `false` when it would not. The comparison is ordinal.

```csharp
PersonalDataMasker.ContainsPersonalData("Nazovite 091 123 4567"); // true
PersonalDataMasker.ContainsPersonalData("Od 2026-10-26 do 26.10.2026."); // false: dates are kept
PersonalDataMasker.ContainsPersonalData("[email] i [phone]"); // false: the placeholders have nothing left to hide
PersonalDataMasker.ContainsPersonalData(null); // false
```

Use it to refuse a value that would carry personal data onward. A search pick or a free-text note is an example. A text that
already holds the placeholders has nothing left to hide, so it is `false`.

Fails: never throws. Null or empty text is `false`.

## Failure summary

| Member | Returns | Throws |
|---|---|---|
| `TextFold.Fold` | The folded text. Null or empty is the empty string | `ArgumentOutOfRangeException` for an undefined `DStroke`; `ArgumentException` for an unpaired surrogate |
| `PersonalDataMasker.Mask` | The masked text. Null or empty is the empty string | Never |
| `PersonalDataMasker.ContainsPersonalData` | `true` or `false`. Null or empty is `false` | Never |

## Using it

- **Matching names.** Fold both sides with the same `DStroke`, then compare. Store the folded form only where you intend to
  match on it.
- **Sending text onward.** Run `Mask` before the text goes into an index, a log or a model, and keep the unmasked original
  where it is needed.
- **Refusing a value.** Use `ContainsPersonalData` and tell the person why the value was refused, rather than masking it
  silently.
