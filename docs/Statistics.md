# Ballast.Statistics: usage guide

Ballast.Statistics holds the plain statistics that rules over counts and spreads need. Every method is a static method on
`Stats`. The package targets `net10.0` and depends on no package.

Install with `dotnet add package Ballast.Statistics --prerelease`.

The examples show the library's actual output, rounded as the examples round it.

## The rules every method follows

- **No NaN.** A method returns a finite number, or null where the quantity is undefined. Nothing is guessed.
- **Out of range throws.** A bad argument throws `ArgumentOutOfRangeException` whose `ParamName` is the argument's name. A
  null argument throws `ArgumentNullException`. An empty list where a value is needed throws `ArgumentException`.
- **Pure.** No state, no clock, no culture. The same input gives the same output.

## `Stats.NormalUpperTail(double z)`

The one-sided upper tail of the standard normal distribution: the probability that a standard normal value is at least `z`,
that is 0.5 × erfc(z / √2).

| `z` | Result |
|---|---|
| `0` | `0.5` |
| `1.96` | `0.0249979` |
| `3.0` | `0.0013499` |
| `-1.96` | `0.9750021` |
| `double.PositiveInfinity` | `0` |
| `double.NegativeInfinity` | `1` |

**How it is computed.** For |x| below 3 (x = z / √2) the complementary error function is 1 − erf(x), with erf from its
Taylor series. From 3 on it is Laplace's continued fraction, e^(−x²) / √π · 1 / (x + (1/2) / (x + 1 / (x + …))), taken to
200 levels. The method is accurate to about 1e-12 absolute.

**How it fails.** `z` is NaN: `ArgumentOutOfRangeException`, `ParamName` `z`.

## `Stats.TwoProportionZ(long seen1, long n1, long seen2, long n2)`

The pooled one-sided z for "share 1 is above share 2":

z = (seen1 / n1 − seen2 / n2) / √(p (1 − p) (1/n1 + 1/n2)), where p = (seen1 + seen2) / (n1 + n2).

A positive z means the first share is higher, and the z is already counted in standard errors. The one-sided p-value that the
first share is higher is `NormalUpperTail(z)`.

| `seen1, n1, seen2, n2` | Result |
|---|---|
| `30, 100, 20, 100` | `1.633` (pooled share 0.25; standard error √0.00375) |
| `20, 100, 30, 100` | `-1.633` |
| `0, 10, 0, 10` | `null`: the pooled share is 0 |
| `10, 10, 20, 20` | `null`: the pooled share is 1 |
| `0, 0, 5, 10` | `null`: the first group has no trials |

**How it fails.**
- A count is negative, or `seen1` or `seen2` is more than its trials: `ArgumentOutOfRangeException`, naming the argument
  (`seen1`, `n1`, `seen2` or `n2`).
- A group has no trials, or the pooled share is 0 or 1: the result is `null`. A null means "no difference can be tested" and
  is never read as zero.

## `Stats.BenjaminiHochberg(IReadOnlyList<double> pValues, double q)`

The Benjamini-Hochberg adjusted p-values of one family of tests, from the step-up procedure of Benjamini and Hochberg (1995).

For each p-value, its rank is its place in ascending order (the largest is rank m, the count). Its adjusted value is the smallest of
m / j × p(j) over every rank j from its own rank up to m, and never more than 1.

| `pValues` | `q` | Result (in input order) |
|---|---|---|
| `[0.01, 0.04, 0.03, 0.005]` | `0.05` | `[0.02, 0.04, 0.04, 0.02]` |
| `[0.9, 0.95, 1.0]` | `0.05` | `[1, 1, 1]`: capped at 1 |
| `[]` | `0.05` | `[]` |

**What `q` does.** The adjusted values do not depend on `q`. Each adjusted value is the smallest false discovery rate at which
its test is rejected, so a test is rejected at rate `q` when its adjusted value is at most `q`. `q` is checked here so that a bad
rate fails where the adjustment is made.

**Properties.** Each adjusted value is at least its p-value and at most 1. Adjusted values never fall as the p-values rise.

**The assumption.** The procedure holds when the tests are independent or positively dependent. Tests that share data may not be,
and their adjusted values are then not guaranteed. The caller says so where it reports the result.

**How it fails.**
- `pValues` is null: `ArgumentNullException`.
- `q` is not above 0 and below 1, or is NaN: `ArgumentOutOfRangeException`, `ParamName` `q`.
- A p-value is NaN or outside [0, 1]: `ArgumentOutOfRangeException`, `ParamName` `pValues`, with the index in the message.

## `Stats.Median(IReadOnlyList<double> values)`

The middle value of the sorted values. With an even count, the mean of the two middle values.

| `values` | Result |
|---|---|
| `[3, 1, 2]` | `2` |
| `[1, 2, 3, 4]` | `2.5` |

**How it fails.**
- `values` is null: `ArgumentNullException`.
- `values` is empty: `ArgumentException` (`ParamName` `values`).
- A value is NaN or infinite: `ArgumentOutOfRangeException`, `ParamName` `values`, with the index in the message.

## `Stats.Mad(IReadOnlyList<double> values)`

The median absolute deviation: the median of the absolute differences from the median of `values`. It is the raw value, with no
scale factor. For normal data, 1.4826 × MAD estimates the standard deviation. `Mad` does not multiply by it.

| `values` | Result |
|---|---|
| `[1, 2, 3, 4, 100]` | `1`: the median is 3, the deviations are 2, 1, 0, 1, 97, and their median is 1 |
| `[1, 2, 3, 4]` | `1`: the median is 2.5, the deviations are 1.5, 0.5, 0.5, 1.5 |
| `[5, 5, 5]` | `0` |

**How it fails.** The same as `Median`: null, empty, or a value that is NaN or infinite.

## Where it is used

The TransitInfoAPI watcher uses `TwoProportionZ`, `NormalUpperTail` and `BenjaminiHochberg` for its live-share test, and `Median`
and `Mad` for its delay test against each station's own day-to-day spread. The thresholds it compares with are its own settings
and are described in `docs/reference/transitinfo-api/realtime.md`.
