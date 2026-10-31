// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Money.Tests;

public class MoneyTextTests
{
    private static readonly CultureInfo _enUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _hrHr = CultureInfo.GetCultureInfo("hr-HR");

    [Fact]
    public void Euros_show_the_euro_sign_and_grouped_thousands()
    {
        Assert.Equal("€1,234.50", MoneyText.Format(new Money(1234.5m, "EUR"), _enUs));
    }

    [Fact]
    public void The_culture_decides_the_separators()
    {
        Assert.Equal("€1.234,50", MoneyText.Format(new Money(1234.5m, "EUR"), _hrHr));
    }

    [Fact]
    public void Dollars_pounds_and_francs_show_their_symbols()
    {
        Assert.Equal("$1,234.50", MoneyText.Format(new Money(1234.5m, "USD"), _enUs));
        Assert.Equal("£1,234.50", MoneyText.Format(new Money(1234.5m, "GBP"), _enUs));
        Assert.Equal("CHF 1,234.50", MoneyText.Format(new Money(1234.5m, "CHF"), _enUs));
    }

    [Fact]
    public void A_retired_currency_still_shows_its_symbol()
    {
        Assert.Equal("kn 1.234,50", MoneyText.Format(new Money(1234.5m, "HRK"), _hrHr));
    }

    [Fact]
    public void A_currency_without_a_symbol_shows_its_code_and_a_space()
    {
        Assert.Equal("BAM 1,234.50", MoneyText.Format(new Money(1234.5m, "BAM"), _enUs));
    }

    [Fact]
    public void Japanese_yen_are_shown_in_whole_units()
    {
        Assert.Equal("JPY 1,235", MoneyText.Format(new Money(1234.5m, "JPY"), _enUs));
    }

    [Fact]
    public void A_known_currency_is_shown_rounded_to_its_minor_units()
    {
        Assert.Equal("€0.13", MoneyText.Format(new Money(0.125m, "EUR"), _enUs));
    }

    [Fact]
    public void A_three_decimal_currency_is_shown_rounded_to_three_places()
    {
        Assert.Equal("KWD 1.235", MoneyText.Format(new Money(1.2345m, "KWD"), _enUs));
    }

    [Fact]
    public void An_unknown_currency_shows_its_code_and_only_the_decimals_its_value_needs()
    {
        Assert.Equal("XYZ 1,234.5", MoneyText.Format(new Money(1234.5m, "XYZ"), _enUs));
        Assert.Equal("XYZ 1,234", MoneyText.Format(new Money(1234m, "XYZ"), _enUs));
        Assert.Equal("XYZ 0.125", MoneyText.Format(new Money(0.125m, "XYZ"), _enUs));
    }

    [Fact]
    public void Equal_amounts_in_an_unknown_currency_show_the_same_text_whatever_their_trailing_zeros()
    {
        Assert.Equal(MoneyText.Format(new Money(1234.5m, "XYZ"), _enUs), MoneyText.Format(new Money(1234.50m, "XYZ"), _enUs));
    }

    [Fact]
    public void An_unknown_currency_follows_the_culture_separators()
    {
        Assert.Equal("XYZ 1.234,5", MoneyText.Format(new Money(1234.5m, "XYZ"), _hrHr));
    }

    [Fact]
    public void A_lower_case_code_shows_the_same_symbol()
    {
        Assert.Equal("€5.00", MoneyText.Format(new Money(5m, "eur"), _enUs));
    }

    [Fact]
    public void A_negative_amount_shows_the_minus_after_the_symbol()
    {
        Assert.Equal("€-1,234.50", MoneyText.Format(new Money(-1234.5m, "EUR"), _enUs));
    }

    [Fact]
    public void A_null_culture_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => MoneyText.Format(new Money(1m, "EUR"), null!));
    }

    [Fact]
    public void A_default_money_is_refused_rather_than_shown_as_zero()
    {
        Assert.Throws<InvalidOperationException>(() => MoneyText.Format(default, _enUs));
    }
}
