# Ballast.Money

Currency codes, rounding to minor units, line totals that match their printed lines, and money formatting. An amount is a
`decimal` in one currency. Nothing is ever converted between currencies.

- `Currencies`: the shape of an ISO 4217 code, and the minor units (decimal places) of the currencies the library knows.
- `Money`: an amount in one currency, with rounding, adding, subtracting and summing.
- `MoneyTotals`: rounds each line first, so the total always equals the sum of the printed lines.
- `MoneyText`: shows an amount with its symbol and the separators of a culture.

It depends on .NET alone, so a server and a phone app can both use it.

## Install

```
dotnet add package Ballast.Money --prerelease
```

## Example

```csharp
using System.Globalization;
using Ballast.Money;

// Round to the currency's minor units, half away from zero.
var price = new Money(0.125m, "eur").Round();
Console.WriteLine(MoneyText.Format(price, CultureInfo.GetCultureInfo("en-US")));

// A printed total always matches its printed lines.
var lines = new[] { new Money(0.333m, "EUR"), new Money(0.333m, "EUR"), new Money(0.333m, "EUR") };
var totals = MoneyTotals.FromLines(lines);
Console.WriteLine(MoneyText.Format(totals.Total, CultureInfo.GetCultureInfo("en-US")));

// The same amount with the separators of another culture.
Console.WriteLine(MoneyText.Format(new Money(1234.5m, "EUR"), CultureInfo.GetCultureInfo("hr-HR")));
```

The output is:

```
€0.13
€0.99
€1.234,50
```

The options of each member, and every way each one fails, are in `docs/Money.md` in the repository.
