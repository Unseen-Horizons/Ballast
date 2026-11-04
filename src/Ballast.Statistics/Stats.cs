// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Statistics;

/// <summary>
/// Plain statistics for rules that compare counts and spreads: the upper tail of the normal distribution, a pooled one-sided
/// two-proportion z, the Benjamini-Hochberg adjustment of several p-values, and the median and the median absolute deviation.
/// </summary>
/// <remarks>
/// <para>
/// Every method is pure and depends on nothing but .NET. A method returns a finite number, or null where the quantity is
/// undefined (<see cref="TwoProportionZ"/>). It never returns NaN. An argument out of range throws
/// <see cref="ArgumentOutOfRangeException"/> that names the argument.
/// </para>
/// </remarks>
public static class Stats
{
    private const double InvSqrtPi = 0.5641895835477563;
    private const double TwoInvSqrtPi = 1.1283791670955126;
    private const double Sqrt2 = 1.4142135623730951;

    /// <summary>The erf series is used below this point, and Laplace's continued fraction from it on.</summary>
    private const double SeriesLimit = 3.0;

    /// <summary>The number of levels of the continued fraction. Its error is far below 1e-12 at the limit after a few dozen levels.</summary>
    private const int ContinuedFractionLevels = 200;

    /// <summary>
    /// The one-sided upper tail of the standard normal distribution: the probability that a standard normal value is at least
    /// <paramref name="z"/>, that is 0.5 × erfc(z / √2).
    /// </summary>
    /// <param name="z">The point to test. Any real number; positive infinity gives 0 and negative infinity gives 1.</param>
    /// <returns>
    /// A probability from 0 to 1. The value is accurate to about 1e-12 absolute. It is 0.5 at <paramref name="z"/> = 0, 0.0249979
    /// at 1.96 and 0.0013499 at 3.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="z"/> is NaN.</exception>
    /// <remarks>
    /// The complementary error function is computed with the Taylor series of erf for |x| below 3, and with Laplace's continued
    /// fraction (erfc(x) = e^(-x²) / √π · 1 / (x + (1/2) / (x + 1 / (x + (3/2) / (x + …))))) from 3 on, as given in Abramowitz and Stegun,
    /// section 7.1, and in Wikipedia's article on the error function. Both are evaluated in double precision.
    /// </remarks>
    public static double NormalUpperTail(double z)
    {
        if (double.IsNaN(z)) throw new ArgumentOutOfRangeException(nameof(z), z, "The z value must not be NaN.");
        return 0.5 * Erfc(z / Sqrt2);
    }

    /// <summary>
    /// The one-sided pooled two-proportion z for "share 1 is above share 2": (p1 − p2) / √(p(1 − p)(1/n1 + 1/n2)), where p is the
    /// pooled share (seen1 + seen2) / (n1 + n2).
    /// </summary>
    /// <param name="seen1">Events in the first group, from 0 to <paramref name="n1"/>.</param>
    /// <param name="n1">Trials in the first group, 0 or more.</param>
    /// <param name="seen2">Events in the second group, from 0 to <paramref name="n2"/>.</param>
    /// <param name="n2">Trials in the second group, 0 or more.</param>
    /// <returns>
    /// The z value, positive when the first share is above the second. Null when the test is undefined: a group has no trials, or
    /// the pooled share is 0 or 1, so the standard error is 0. A null is never a guess.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative, or <paramref name="seen1"/> or <paramref name="seen2"/> is more than its trials.</exception>
    /// <example>
    /// <code>
    /// // 30 of 100 against 20 of 100: pooled share 0.25, standard error sqrt(0.25 × 0.75 × 0.02), z = 1.633
    /// var z = Stats.TwoProportionZ(30, 100, 20, 100);
    /// </code>
    /// </example>
    public static double? TwoProportionZ(long seen1, long n1, long seen2, long n2)
    {
        RequireCount(seen1, n1, nameof(seen1), nameof(n1));
        RequireCount(seen2, n2, nameof(seen2), nameof(n2));
        if (n1 == 0 || n2 == 0) return null;

        var seen = seen1 + seen2;
        var total = n1 + n2;
        if (seen == 0 || seen == total) return null;

        var pooled = seen / (double)total;
        var standardError = Math.Sqrt(pooled * (1.0 - pooled) * (1.0 / n1 + 1.0 / n2));
        return (seen1 / (double)n1 - seen2 / (double)n2) / standardError;
    }

    /// <summary>
    /// The Benjamini-Hochberg adjusted p-value of each p-value: the smallest false discovery rate at which that test would be
    /// rejected, from the step-up procedure of Benjamini and Hochberg (1995).
    /// </summary>
    /// <param name="pValues">The p-values of one family of tests, each from 0 to 1, in any order. May be empty.</param>
    /// <param name="q">The false discovery rate the caller will compare with, above 0 and below 1.</param>
    /// <returns>
    /// The adjusted values in the order of <paramref name="pValues"/>. Each is from the p-value to 1, at most 1, and the adjusted
    /// values never fall as the p-values rise. A test is rejected at rate <paramref name="q"/> when its adjusted value is at most
    /// <paramref name="q"/>. Empty input gives an empty list.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="pValues"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="q"/> is not above 0 and below 1, or a p-value is NaN or outside [0, 1].
    /// </exception>
    /// <remarks>
    /// <para>
    /// The adjusted value of the test with rank i (its place among the p-values in ascending order, the largest being rank m) is the
    /// smallest of m / j × p(j) over every rank j from i up to m, and never more than 1. The values do not depend on <paramref name="q"/>:
    /// it is checked here so that a bad rate fails where the adjustment is made, and the caller compares the result with it.
    /// </para>
    /// <para>
    /// The procedure assumes the tests are independent or positively dependent. Tests that share data may not be, and their adjusted
    /// values are then not guaranteed.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<double> BenjaminiHochberg(IReadOnlyList<double> pValues, double q)
    {
        ArgumentNullException.ThrowIfNull(pValues);
        if (!(q > 0 && q < 1)) throw new ArgumentOutOfRangeException(nameof(q), q, "The false discovery rate q must be above 0 and below 1.");

        var m = pValues.Count;
        var adjusted = new double[m];
        for (var i = 0; i < m; i++)
        {
            var p = pValues[i];
            if (!(p >= 0 && p <= 1)) throw new ArgumentOutOfRangeException(nameof(pValues), p, $"The p-value at index {i} must be from 0 to 1.");
        }
        if (m == 0) return adjusted;

        var order = Enumerable.Range(0, m).OrderBy(i => pValues[i]).ToArray();
        var running = 1.0;
        for (var rank = m; rank >= 1; rank--)
        {
            var index = order[rank - 1];
            running = Math.Min(running, pValues[index] * m / rank);
            adjusted[index] = running;
        }
        return adjusted;
    }

    /// <summary>
    /// The median: the middle value of the sorted values, or the mean of the two middle values when there is an even count.
    /// </summary>
    /// <param name="values">The values. Must not be empty, and every value must be finite.</param>
    /// <returns>The median, a finite number.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value is NaN or infinite.</exception>
    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = SortedFinite(values, nameof(values));
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : sorted[middle - 1] / 2 + sorted[middle] / 2;
    }

    /// <summary>
    /// The median absolute deviation: the median of the absolute differences from the median. It is the raw value, with no scale
    /// factor. Multiply it by 1.4826 to estimate a standard deviation for normal data.
    /// </summary>
    /// <param name="values">The values. Must not be empty, and every value must be finite.</param>
    /// <returns>The median absolute deviation, a finite number of 0 or more. It is 0 when most values are equal to the median.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value is NaN or infinite.</exception>
    public static double Mad(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var median = Median(values);
        var deviations = new double[values.Count];
        for (var i = 0; i < deviations.Length; i++) deviations[i] = Math.Abs(values[i] - median);
        return Median(deviations);
    }

    private static void RequireCount(long seen, long trials, string seenName, string trialsName)
    {
        if (trials < 0) throw new ArgumentOutOfRangeException(trialsName, trials, "A count must be 0 or more.");
        if (seen < 0 || seen > trials) throw new ArgumentOutOfRangeException(seenName, seen, "Events must be from 0 to the trials in the same group.");
    }

    private static double[] SortedFinite(IReadOnlyList<double> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) throw new ArgumentException("The list must not be empty.", paramName);

        var copy = new double[values.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            var value = values[i];
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(paramName, value, $"The value at index {i} must be a finite number.");
            copy[i] = value;
        }
        Array.Sort(copy);
        return copy;
    }

    /// <summary>erfc(x) for any real x: 2 − erfc(−x) for negative x, the series below the limit, the continued fraction above it.</summary>
    private static double Erfc(double x)
    {
        if (x < 0) return 2.0 - Erfc(-x);
        if (x < SeriesLimit) return 1.0 - Erf(x);

        var denominator = x;
        for (var k = ContinuedFractionLevels; k >= 1; k--) denominator = x + (k / 2.0) / denominator;
        return Math.Exp(-x * x) * InvSqrtPi / denominator;
    }

    /// <summary>erf(x) for 0 ≤ x &lt; 3, by its Taylor series: 2/√π · Σ (−1)^n x^(2n+1) / (n! (2n+1)).</summary>
    private static double Erf(double x)
    {
        var term = x;
        var sum = 0.0;
        for (var n = 0; n < 500; n++)
        {
            sum += term / (2 * n + 1);
            term = -term * x * x / (n + 1);
            if (Math.Abs(term) < 1e-18) break;
        }
        return TwoInvSqrtPi * sum;
    }
}
