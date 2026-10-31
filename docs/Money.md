# Ballast.Money: usage guide

`Ballast.Money` holds an amount of money in one currency, rounds it to the currency's minor units, adds up lines so that a
printed total matches its printed lines, and formats an amount for display. It has the namespace `Ballast.Money`, targets
`net10.0`, and depends on no package.

Install it with:

```
dotnet add package Ballast.Money --prerelease
```

The package README has a short example that runs as written. This guide covers every public member, what it accepts, and
every way it fails.

## Conventions

- **Amounts are `decimal`.** Never `double`. An amount is in whole units of its currency: `2.50` is two euros and fifty cents.
- **Currencies are ISO 4217 codes.** A code is exactly three ASCII letters. Case does not matter, and the library stores it in
  upper case, so `eur` and `EUR` are the same currency.
- **Nothing is converted.** Two amounts in different currencies cannot be added, subtracted, summed or totalled together. The
  library raises an error rather than convert them. A caller that needs a conversion does it with its own rate, before it adds.
- **Rounding is half away from zero.** 0.125 becomes 0.13, and -0.125 becomes -0.13. It is applied only when asked: an amount is
  stored exactly as given.
- **Minor units.** The number of decimal places a currency is rounded to, from the table under "The minor units the library knows". A
  code outside the table has no known number of decimal places, and it is never assumed to have two.
- **No options.** None of the members takes a setting that changes the rounding or the format. The culture for formatting is the
  one required argument.

### The minor units the library knows

The table is ISO 4217 List One, published on 2026-09-17 by SIX, the ISO 4217 maintenance agency. It holds the 165 codes that have
minor units: EUR is 2, KWD is 3 and JPY is 0. The list is not copied here. `Currencies.MinorUnits` is the authority for each code, and
the table is fixed in each version of the library. A later version takes the next List One.

Codes that have no minor units are left out, so `MinorUnits` gives `null` for them: the precious metals XAG, XAU, XPD and XPT, the
bond-market units XBA, XBB, XBC and XBD, and the unit and non-currency codes XDR, XSU, XTS, XUA and XXX.

Two codes outside the list are kept, both with 2 minor units, because amounts recorded in them still exist: HRK, withdrawn on
2023-01-01, and BGN, withdrawn on 2026-01-01.

Any other code has no known minor units. `Round` refuses it, and `MoneyText.Format` shows it with the decimal places its value
needs. Both behaviours are described under each member.

---

## Currencies

A static class with two members: one checks the shape of a code, and one gives the minor units of a code the library knows.

### `Currencies.IsIsoCode(string? code)` → `bool`

Whether a code has the shape of an ISO 4217 alphabetic code: exactly three ASCII letters, in any case.

- **Returns `true`** for `EUR`, `eur` and `XYZ`. The shape is right for `XYZ` even though the library does not know it.
- **Returns `false`** for `null`, an empty string, a blank string, a code of the wrong length, a character that is not an ASCII letter
  (`E1R`, `ÉUR`), or a code with a space in it (` EU`).
- **Never throws.**

It answers the shape only. To ask whether the library knows a code's rounding, use `MinorUnits`.

```csharp
Currencies.IsIsoCode("EUR");    // true
Currencies.IsIsoCode("eur");    // true
Currencies.IsIsoCode("XYZ");    // true: the shape is right
Currencies.IsIsoCode("EURO");   // false: four letters
Currencies.IsIsoCode(null);     // false
```

### `Currencies.MinorUnits(string? code)` → `int?`

The number of minor units (decimal places) of a currency: 2 for EUR, 0 for JPY.

- **Returns** the minor units of a code in the table, in any case, or `null` for a code outside the table.
- **Returns `null`** for `null`, an empty string, and any code not in the table. A code outside the table is never given two
  decimal places.
- **Never throws.**

```csharp
Currencies.MinorUnits("EUR");    // 2
Currencies.MinorUnits("jpy");    // 0: the case does not matter
Currencies.MinorUnits("SEK");    // 2: in the table
Currencies.MinorUnits("XAU");    // null: not in the table
Currencies.MinorUnits(null);     // null
```

---

## Money

An amount in one currency: a `decimal` and an ISO 4217 code. It is a `readonly record struct`, so two amounts are equal when their
amounts and currencies are equal. `1.0` and `1.00` are the same amount.

### `new Money(decimal amount, string? currency)`

Creates an amount.

- **`amount`** is stored exactly as given. It is not rounded, so an amount with more decimals than its currency has keeps them until
  `Round` is called.
- **`currency`** must be exactly three ASCII letters, in any case. It is stored in upper case.

**Throws `ArgumentException`** (`ParamName` is `currency`) when the currency is:
- `null`, or blank (empty, or only spaces);
- not exactly three ASCII letters: `EURO`, `EU`, `E1R`, ` EUR` (with a space), `€UR`.

Nothing is trimmed or guessed. `" EUR"` is refused, not read as `EUR`.

```csharp
var price = new Money(0.125m, "eur");   // Amount 0.125, Currency "EUR"
new Money(1m, "EURO");                  // throws ArgumentException: four letters
new Money(1m, null);                    // throws ArgumentException: blank
```

### `Money.Amount` and `Money.Currency`

- **`Amount`** is the `decimal` amount, in whole units of the currency. It is the amount as stored: not rounded.
- **`Currency`** is the code in upper case. It is `null` only for a `default(Money)` (see below).

### `Round()` → `Money`

Rounds the amount to the currency's minor units, half away from zero. It returns a new amount in the same currency.

- 0.125 EUR becomes 0.13 EUR; -0.125 EUR becomes -0.13 EUR.
- 2.345 EUR becomes 2.35 EUR; 1.005 USD becomes 1.01 USD.
- 1234.5 JPY becomes 1235 JPY; 1234.4 JPY becomes 1234 JPY; -2.5 JPY becomes -3 JPY.

**Throws `InvalidOperationException`** when:
- the amount is a `default(Money)`, which has no currency;
- the currency is not in `Currencies.MinorUnits`. The library does not guess a number of decimal places for a code it does not know.

```csharp
new Money(0.125m, "EUR").Round();     // 0.13 EUR
new Money(1234.5m, "JPY").Round();    // 1235 JPY
new Money(1.5m, "XYZ").Round();       // throws InvalidOperationException: XYZ has no known minor units
```

### `Add(Money other)` → `Money`

Adds an amount in the same currency. Nothing is rounded.

**Throws `InvalidOperationException`** when the two amounts have different currencies, or either is a `default(Money)`. The message
names both currencies. Nothing is converted. It throws `OverflowException` when the sum does not fit in a `decimal`, as `decimal`
arithmetic does.

```csharp
new Money(0.125m, "EUR").Add(new Money(0.125m, "EUR"));   // 0.25 EUR: nothing is rounded
new Money(1m, "EUR").Add(new Money(1m, "USD"));           // throws InvalidOperationException
```

### `Subtract(Money other)` → `Money`

Subtracts an amount in the same currency. Nothing is rounded. It fails in the same ways as `Add`.

```csharp
new Money(1m, "EUR").Subtract(new Money(2m, "EUR"));   // -1 EUR
```

### `Money.Sum(IEnumerable<Money> amounts)` → `Money`

Adds up amounts that share one currency. Nothing is rounded.

**Throws:**
- `ArgumentNullException` when `amounts` is `null`.
- `ArgumentException` when `amounts` is empty. The amounts give the currency, so an empty sum has none, and the caller decides what
  to show for an empty list.
- `InvalidOperationException` when the amounts do not share one currency, or one of them is a `default(Money)`.
- `OverflowException` when the sum does not fit in a `decimal`.

```csharp
Money.Sum([new Money(1m, "EUR"), new Money(2m, "EUR"), new Money(3m, "EUR")]);   // 6 EUR
Money.Sum([new Money(0.125m, "EUR"), new Money(0.125m, "EUR"), new Money(0.125m, "EUR")]);   // 0.375 EUR, not rounded
Money.Sum([]);                                                                  // throws ArgumentException
```

### `default(Money)`

A default `Money` has an amount of zero and no currency (`Currency` is `null`). It is not zero euros. Every operation on it throws
`InvalidOperationException`: `Round`, `Add`, `Subtract`, `Sum`, `MoneyTotals.FromLines` and `MoneyText.Format`. Create an amount with
the constructor.

---

## MoneyTotals

The rounded lines of an invoice or a receipt, and their total. The total always equals the sum of the rounded lines, so a printed
total always matches the printed lines.

A total computed from unrounded lines can differ by a minor unit from the lines a reader adds up. Two lines of 10.005 EUR round to
10.01 EUR each, and their rounded sum is 20.02 EUR. Rounding the unrounded sum, 20.010, gives 20.01 EUR, which does not match the lines.

The class is sealed, and only `FromLines` makes one, so the lines and the total always agree.

### `MoneyTotals.Lines` → `IReadOnlyList<Money>`

The lines, each rounded to its currency's minor units, in the order they were given. The list is read-only.

### `MoneyTotals.Total` → `Money`

The sum of the rounded lines, in their currency.

### `MoneyTotals.FromLines(IEnumerable<Money> lines)` → `MoneyTotals`

Rounds each line, then adds the rounded lines.

**Throws:**
- `ArgumentNullException` when `lines` is `null`.
- `ArgumentException` when `lines` is empty. A line gives the currency, so an empty invoice has no currency to total in.
- `InvalidOperationException` when the lines do not share one currency, when a line is a `default(Money)`, or when a line's currency has no
  known minor units (see `Money.Round`).

```csharp
var totals = MoneyTotals.FromLines([new Money(0.333m, "EUR"), new Money(0.333m, "EUR"), new Money(0.333m, "EUR")]);
// Lines: 0.33, 0.33, 0.33 EUR
// Total: 0.99 EUR. The unrounded lines add to 0.999, which would round to 1.00, so it is not used.

MoneyTotals.FromLines([]);                                            // throws ArgumentException
MoneyTotals.FromLines([new Money(1m, "EUR"), new Money(1m, "USD")]);  // throws InvalidOperationException
```

`FromLines` sums the amounts it is given. It does not compute tax: an invoice that shows VAT on each line passes the amounts it
prints.

---

## MoneyText

Shows an amount as text: a currency symbol, then the number in the culture's separators.

### `MoneyText.Format(Money money, CultureInfo culture)` → `string`

Formats an amount.

- **Symbols.** EUR is `€`, USD is `$`, GBP is `£`, CHF is `CHF ` (with a space), and HRK is `kn ` (with a space). HRK keeps its symbol so
  that amounts recorded before it was retired still show it. The symbol comes first, with no space, except where the symbol itself
  ends in one.
- **Other currencies** show their code and a space, so `XYZ 1,234.5`.
- **Known currencies** show their minor units, after the rounding of `Round`. `JPY 1,235`, and `€0.13` for 0.125 EUR. The text is
  rounded for display, and the amount itself is not changed.
- **Unknown currencies** show the decimal places the value needs, without trailing zeros, and nothing is rounded. `XYZ 1,234.5` for
  1234.5 and for 1234.50 alike, and `XYZ 0.125` for 0.125. Equal amounts show the same text.
- **Separators** come from `culture`. `en-US` gives `€1,234.50`; `hr-HR` gives `€1.234,50`.
- **A negative amount** shows the culture's minus sign after the symbol: `€-1,234.50`.

**Throws:**
- `ArgumentNullException` when `culture` is `null`. The culture is required, so the text never depends on the machine's own culture.
- `InvalidOperationException` when the amount is a `default(Money)`. It is not shown as zero.

```csharp
MoneyText.Format(new Money(1234.5m, "EUR"), CultureInfo.GetCultureInfo("en-US"));   // €1,234.50
MoneyText.Format(new Money(1234.5m, "EUR"), CultureInfo.GetCultureInfo("hr-HR"));   // €1.234,50
MoneyText.Format(new Money(1234.5m, "CHF"), CultureInfo.GetCultureInfo("en-US"));   // CHF 1,234.50
MoneyText.Format(new Money(1234.5m, "JPY"), CultureInfo.GetCultureInfo("en-US"));   // JPY 1,235
MoneyText.Format(new Money(1234.5m, "XYZ"), CultureInfo.GetCultureInfo("en-US"));   // XYZ 1,234.5
MoneyText.Format(new Money(-1234.5m, "EUR"), CultureInfo.GetCultureInfo("en-US"));  // €-1,234.50
MoneyText.Format(default, CultureInfo.GetCultureInfo("en-US"));                     // throws InvalidOperationException
```

---

## Ways it fails

| Member | Refuses with | Notes |
|---|---|---|
| `new Money(amount, currency)` | `ArgumentException` (`currency`) | null, blank, or not three ASCII letters |
| `Currencies.IsIsoCode` | nothing: never throws | `false` for a code of the wrong shape |
| `Currencies.MinorUnits` | nothing: never throws | `null` for a code outside the table |
| `Money.Round` | `InvalidOperationException` | a `default(Money)`, or a currency with no known minor units |
| `Money.Add`, `Money.Subtract` | `InvalidOperationException` | different currencies, or a `default(Money)`; `OverflowException` for a value that does not fit a `decimal` |
| `Money.Sum` | `ArgumentNullException`, `ArgumentException` (empty), `InvalidOperationException` (mixed currencies, or a `default(Money)`), `OverflowException` | nothing is rounded or converted |
| `MoneyTotals.FromLines` | `ArgumentNullException`, `ArgumentException` (empty), `InvalidOperationException` (mixed currencies, a `default(Money)`, or no known minor units) | the total is never computed from unrounded lines |
| `MoneyText.Format` | `ArgumentNullException` (`culture`), `InvalidOperationException` (a `default(Money)`) | an unknown code is shown, not refused |

---

## Not in this library

- Exchange rates and conversion between currencies.
- The list of currencies an application accepts. That is a setting of the application.
- Tax, VAT and discount calculation.
- Currency codes that are not on ISO 4217 List One of 2026-09-17, except HRK and BGN, and any list published after it. A later List One is
  taken in a later version of the library.
- Symbols beyond the five in the table. Every other currency shows its code.
- Currencies whose rules change over time.
