// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Money;

/// <summary>
/// An amount of money: a <see cref="decimal"/> in one currency, named by its ISO 4217 code.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here converts between currencies. Adding, subtracting or summing amounts in different currencies is an error,
/// and the caller converts with its own rate before it adds.
/// </para>
/// <para>
/// The amount is kept exactly as given. Rounding is a step the caller takes with <see cref="Round"/>, so an amount with more
/// decimals than its currency has is not changed until then.
/// </para>
/// <para>
/// <c>default(Money)</c> has no currency. Every operation on it throws <see cref="InvalidOperationException"/>, so it cannot
/// pass for zero euros. Create an amount with the constructor.
/// </para>
/// </remarks>
public readonly record struct Money
{
    /// <summary>
    /// Creates an amount in one currency.
    /// </summary>
    /// <param name="amount">
    /// The amount in whole units of the currency: 2.50 is two euros and fifty cents. It is stored as given.
    /// </param>
    /// <param name="currency">
    /// The ISO 4217 alphabetic code: exactly three ASCII letters, in any case. It is stored in upper case, so <c>eur</c> and
    /// <c>EUR</c> are the same currency.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="currency"/> is null, blank, or not exactly three ASCII letters. Nothing is trimmed or guessed.
    /// </exception>
    public Money(decimal amount, string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("The currency code must not be blank.", nameof(currency));
        if (!Currencies.IsIsoCode(currency))
            throw new ArgumentException("The currency code must be exactly three ASCII letters, such as EUR.", nameof(currency));

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    /// <summary>The amount, in whole units of the currency. Not rounded.</summary>
    public decimal Amount { get; }

    /// <summary>The ISO 4217 code in upper case, such as <c>EUR</c>. Null only for a <c>default(Money)</c>.</summary>
    public string Currency { get; }

    /// <summary>
    /// The amount rounded to the currency's minor units, half away from zero: 0.125 EUR becomes 0.13 EUR, and -0.125 EUR
    /// becomes -0.13 EUR. JPY rounds to whole yen.
    /// </summary>
    /// <returns>A new amount in the same currency, rounded.</returns>
    /// <exception cref="InvalidOperationException">
    /// The amount is a <c>default(Money)</c>, or its currency is not in <see cref="Currencies.MinorUnits"/>, so its decimal
    /// places are not known. The library does not guess them.
    /// </exception>
    public Money Round()
    {
        var currency = RequireCurrency();
        var digits = Currencies.MinorUnits(currency)
            ?? throw new InvalidOperationException($"The minor units of {currency} are not known, so the amount cannot be rounded.");
        return new Money(decimal.Round(Amount, digits, MidpointRounding.AwayFromZero), currency);
    }

    /// <summary>Adds an amount in the same currency. Nothing is rounded.</summary>
    /// <param name="other">The amount to add.</param>
    /// <returns>The sum, in this amount's currency.</returns>
    /// <exception cref="InvalidOperationException">
    /// The two amounts have different currencies, or either is a <c>default(Money)</c>. Nothing is converted.
    /// </exception>
    /// <exception cref="OverflowException">The sum does not fit in a <see cref="decimal"/>.</exception>
    public Money Add(Money other)
    {
        var currency = SameCurrency(this, other);
        return new Money(Amount + other.Amount, currency);
    }

    /// <summary>Subtracts an amount in the same currency. Nothing is rounded.</summary>
    /// <param name="other">The amount to subtract.</param>
    /// <returns>The difference, in this amount's currency.</returns>
    /// <exception cref="InvalidOperationException">
    /// The two amounts have different currencies, or either is a <c>default(Money)</c>. Nothing is converted.
    /// </exception>
    /// <exception cref="OverflowException">The difference does not fit in a <see cref="decimal"/>.</exception>
    public Money Subtract(Money other)
    {
        var currency = SameCurrency(this, other);
        return new Money(Amount - other.Amount, currency);
    }

    /// <summary>Adds up amounts that share one currency. Nothing is rounded.</summary>
    /// <param name="amounts">
    /// The amounts. At least one is needed, because an amount is what gives the sum its currency.
    /// </param>
    /// <returns>The sum, in the currency of the amounts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="amounts"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="amounts"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The amounts do not all share one currency, or one of them is a <c>default(Money)</c>.
    /// </exception>
    /// <exception cref="OverflowException">The sum does not fit in a <see cref="decimal"/>.</exception>
    public static Money Sum(IEnumerable<Money> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        Money? total = null;
        foreach (var amount in amounts)
            total = total is { } running ? running.Add(amount) : Checked(amount);
        return total ?? throw new ArgumentException("A sum needs at least one amount, because an amount is what gives the currency.", nameof(amounts));
    }

    /// <summary>The currency, or a throw when this is a default value with none.</summary>
    internal string RequireCurrency() =>
        Currency ?? throw new InvalidOperationException("This Money is a default value and has no currency. Create an amount with the constructor.");

    private static Money Checked(Money amount) => new(amount.Amount, amount.RequireCurrency());

    private static string SameCurrency(Money left, Money right)
    {
        var leftCurrency = left.RequireCurrency();
        var rightCurrency = right.RequireCurrency();
        if (!string.Equals(leftCurrency, rightCurrency, StringComparison.Ordinal))
            throw new InvalidOperationException($"Cannot combine {leftCurrency} and {rightCurrency}. Nothing is converted between currencies.");
        return leftCurrency;
    }
}
