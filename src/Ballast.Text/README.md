# Ballast.Text

Text for comparing, and text for sending onward.

- **TextFold.Fold** folds text so that two spellings of one name compare equal. Accents, letter case, apostrophes,
  punctuation and the amount of spacing do not count: "Trg bana Jelačića" and "trg bana jelacica" fold to the same text.
  The letter đ folds to `d`, or to `dj` when you ask for it.
- **PersonalDataMasker.Mask** hides email addresses, phone numbers and links before a text goes into a search index, a log
  or a model. Dates, times, line numbers and prices are kept. `ContainsPersonalData` says whether a text has anything to
  hide.

Letters are never changed by the masker, so text in any script passes through it. The folded form is for comparing, not for
showing: it is not a transliteration.

Preview: version 0.1.0-preview.1. The usage guide, with every option and every way the library fails, is `docs/Text.md`
in the repository.

## Install

```
dotnet add package Ballast.Text --prerelease
```

## Example

```csharp
using Ballast.Text;

Console.WriteLine(TextFold.Fold("Trg bana Jelačića"));  // trg bana jelacica
Console.WriteLine(TextFold.Fold("Đakovo", DStroke.Dj)); // djakovo

var note = "Nazovite +385 91 123 4567 ili ana@example.com, 26.10.2026.";
Console.WriteLine(PersonalDataMasker.Mask(note));       // Nazovite [phone] ili [email], 26.10.2026.
Console.WriteLine(PersonalDataMasker.ContainsPersonalData(note)); // True
```
