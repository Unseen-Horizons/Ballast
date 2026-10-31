// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Money;

/// <summary>
/// The rounded lines of an invoice or receipt, and their total. The total always equals the sum of the rounded lines.
/// </summary>
/// <remarks>
/// <para>
/// A total computed from unrounded lines can differ by a minor unit from the lines a reader adds up. Here each line is rounded
/// first, and the total is the sum of the rounded lines, so the printed total always matches the printed lines.
/// </para>
/// <para>
/// Only <see cref="FromLines"/> makes one, so the lines and the total always agree.
/// </para>
/// </remarks>
public sealed class MoneyTotals
{
    private MoneyTotals(IReadOnlyList<Money> lines, Money total)
    {
        Lines = lines;
        Total = total;
    }

    /// <summary>The lines, each rounded to its currency's minor units, in the order they were given.</summary>
    public IReadOnlyList<Money> Lines { get; }

    /// <summary>The sum of the rounded lines, in their currency.</summary>
    public Money Total { get; }

    /// <summary>
    /// Rounds each line, then adds the rounded lines.
    /// </summary>
    /// <param name="lines">
    /// The lines, all in one currency. At least one is needed, because a line is what gives the currency.
    /// </param>
    /// <returns>The rounded lines and their total.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="lines"/> is empty. An empty invoice has no currency to total in, so the caller decides what to show.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The lines do not share one currency, a line is a <c>default(Money)</c>, or a line's currency has no known rounding
    /// (see <see cref="Money.Round"/>).
    /// </exception>
    public static MoneyTotals FromLines(IEnumerable<Money> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<Money> rounded = [];
        Money? total = null;
        foreach (var line in lines)
        {
            var roundedLine = line.Round();
            total = total is { } running ? running.Add(roundedLine) : roundedLine;
            rounded.Add(roundedLine);
        }

        if (total is not { } sum)
            throw new ArgumentException("A total needs at least one line, because a line is what gives the currency.", nameof(lines));
        return new MoneyTotals(rounded.AsReadOnly(), sum);
    }
}
