# Ballast.Statistics

Plain statistics for rules that compare counts and spreads, with no dependency.

- **Stats.NormalUpperTail** gives the one-sided probability that a standard normal value is at least a point. It is accurate
  to about 1e-12.
- **Stats.TwoProportionZ** gives the pooled one-sided z for "share 1 is above share 2". It is null when the test is undefined
  (a group has no trials, or the pooled share is 0 or 1), never a guess.
- **Stats.BenjaminiHochberg** adjusts several p-values together, so that a family of tests keeps its false discovery rate.
- **Stats.Median** and **Stats.Mad** give the median and the raw median absolute deviation (no scale factor).

No method returns NaN. An argument out of range throws `ArgumentOutOfRangeException` that names it.

Preview: version 0.1.0-preview.3. The usage guide, with every method, its inputs and how it fails, is `docs/Statistics.md`
in the repository.

## Install

```
dotnet add package Ballast.Statistics --prerelease
```

## Example

```csharp
using Ballast.Statistics;

Console.WriteLine(Math.Round(Stats.NormalUpperTail(1.96), 4));           // 0.025
var z = Stats.TwoProportionZ(30, 100, 20, 100);
Console.WriteLine(z is null ? "undefined" : Math.Round(z.Value, 3));    // 1.633
var adjusted = Stats.BenjaminiHochberg([0.01, 0.04, 0.03, 0.005], 0.05);
Console.WriteLine(string.Join(" ", adjusted.Select(p => Math.Round(p, 2)))); // 0.02 0.04 0.04 0.02
Console.WriteLine(Stats.Median([3.0, 1.0, 2.0]));                       // 2
Console.WriteLine(Stats.Mad([1.0, 2.0, 3.0, 4.0, 100.0]));              // 1
```
