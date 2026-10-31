// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Money;

/// <summary>
/// Shows an amount as text: a currency symbol, then the number in the culture's own separators.
/// </summary>
/// <remarks>
/// <para>
/// The symbols are EUR "€", USD "$", GBP "£", CHF "CHF " and HRK "kn ". The symbol comes first. Any other currency shows its
/// code and a space instead, so <c>XYZ 1,234.5</c>.
/// </para>
/// <para>
/// A known currency is shown with its minor units, after the same half-away-from-zero rounding as <see cref="Money.Round"/>.
/// An unknown currency is shown with the decimal places its value needs, without trailing zeros, and nothing is rounded.
/// Equal amounts therefore always show the same text, whatever the number of zeros they were written with.
/// </para>
/// </remarks>
public static class MoneyText
{
    /// <summary>The number format for an unknown currency: grouped thousands, and as many decimals as the value needs (up to 28).</summary>
    private const string UnknownCurrencyFormat = "#,0.############################";

    /// <summary>
    /// Formats an amount with its currency symbol and the culture's separators.
    /// </summary>
    /// <param name="money">The amount to show.</param>
    /// <param name="culture">
    /// The culture for the group and decimal separators, such as <c>en-US</c> (1,234.50) or <c>hr-HR</c> (1.234,50). It is
    /// required, so the text never depends on the machine's own culture.
    /// </param>
    /// <returns>
    /// The symbol or code, then the number. A negative amount shows the culture's minus sign after the symbol, as in
    /// <c>€-1.00</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The amount is a <c>default(Money)</c>, which has no currency.</exception>
    public static string Format(Money money, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var currency = money.RequireCurrency();
        var symbol = SymbolFor(currency) ?? currency + " ";
        var number = Currencies.MinorUnits(currency) is { } digits
            ? money.Round().Amount.ToString("N" + digits.ToString(CultureInfo.InvariantCulture), culture)
            : money.Amount.ToString(UnknownCurrencyFormat, culture);
        return symbol + number;
    }

    /// <summary>The symbol of a known currency, with its trailing space where the symbol needs one, or null.</summary>
    private static string? SymbolFor(string currency) => currency switch
    {
        "EUR" => "€",
        "USD" => "$",
        "GBP" => "£",
        "CHF" => "CHF ",
        "HRK" => "kn ",
        _ => null,
    };
}
