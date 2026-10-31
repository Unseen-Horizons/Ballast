// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Money.Tests;

public class MoneyTests
{
    [Fact]
    public void The_code_is_stored_in_upper_case()
    {
        Assert.Equal("EUR", new Money(1m, "eur").Currency);
    }

    [Fact]
    public void The_amount_is_kept_exactly_as_given()
    {
        Assert.Equal(0.125m, new Money(0.125m, "EUR").Amount);
    }

    [Fact]
    public void The_same_currency_in_either_case_is_the_same_money()
    {
        Assert.Equal(new Money(1m, "EUR"), new Money(1m, "eur"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData(" EUR")]
    [InlineData("€UR")]
    public void A_currency_that_is_not_three_ASCII_letters_is_refused(string? currency)
    {
        var error = Assert.Throws<ArgumentException>(() => new Money(1m, currency));
        Assert.Equal("currency", error.ParamName);
    }

    [Fact]
    public void A_quarter_of_a_cent_rounds_up_to_the_next_cent()
    {
        Assert.Equal(new Money(0.13m, "EUR"), new Money(0.125m, "EUR").Round());
    }

    [Fact]
    public void A_negative_quarter_of_a_cent_rounds_away_from_zero()
    {
        Assert.Equal(new Money(-0.13m, "EUR"), new Money(-0.125m, "EUR").Round());
    }

    [Fact]
    public void Rounding_to_cents_is_half_away_from_zero_on_every_half()
    {
        Assert.Equal(new Money(2.35m, "EUR"), new Money(2.345m, "EUR").Round());
        Assert.Equal(new Money(1.01m, "USD"), new Money(1.005m, "USD").Round());
    }

    [Fact]
    public void Japanese_yen_round_to_whole_yen()
    {
        Assert.Equal(new Money(1235m, "JPY"), new Money(1234.5m, "JPY").Round());
        Assert.Equal(new Money(1234m, "JPY"), new Money(1234.4m, "JPY").Round());
        Assert.Equal(new Money(-3m, "JPY"), new Money(-2.5m, "JPY").Round());
    }

    [Fact]
    public void Rounding_keeps_the_currency()
    {
        Assert.Equal("JPY", new Money(1.5m, "jpy").Round().Currency);
    }

    [Fact]
    public void An_amount_in_a_currency_with_no_known_minor_units_cannot_be_rounded()
    {
        Assert.Throws<InvalidOperationException>(() => new Money(1.005m, "XYZ").Round());
    }

    [Fact]
    public void Adding_two_amounts_in_the_same_currency_adds_them()
    {
        Assert.Equal(new Money(3m, "EUR"), new Money(1m, "EUR").Add(new Money(2m, "eur")));
    }

    [Fact]
    public void Adding_rounds_nothing()
    {
        Assert.Equal(0.25m, new Money(0.125m, "EUR").Add(new Money(0.125m, "EUR")).Amount);
    }

    [Fact]
    public void Subtracting_two_amounts_in_the_same_currency_subtracts_them()
    {
        Assert.Equal(new Money(-1m, "EUR"), new Money(1m, "EUR").Subtract(new Money(2m, "EUR")));
    }

    [Fact]
    public void Adding_two_currencies_throws_and_converts_nothing()
    {
        Assert.Throws<InvalidOperationException>(() => new Money(1m, "EUR").Add(new Money(1m, "USD")));
        Assert.Throws<InvalidOperationException>(() => new Money(0m, "EUR").Add(new Money(5m, "USD")));
    }

    [Fact]
    public void Subtracting_two_currencies_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new Money(1m, "EUR").Subtract(new Money(1m, "USD")));
    }

    [Fact]
    public void Sum_adds_amounts_that_share_a_currency()
    {
        Assert.Equal(new Money(6m, "EUR"), Money.Sum([new Money(1m, "EUR"), new Money(2m, "EUR"), new Money(3m, "EUR")]));
    }

    [Fact]
    public void Sum_rounds_nothing()
    {
        Assert.Equal(new Money(0.375m, "EUR"), Money.Sum([new Money(0.125m, "EUR"), new Money(0.125m, "EUR"), new Money(0.125m, "EUR")]));
    }

    [Fact]
    public void Sum_of_no_amounts_is_refused_because_there_is_no_currency()
    {
        Assert.Throws<ArgumentException>(() => Money.Sum([]));
    }

    [Fact]
    public void Sum_of_mixed_currencies_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Money.Sum([new Money(1m, "EUR"), new Money(1m, "USD")]));
    }

    [Fact]
    public void Sum_of_null_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => Money.Sum(null!));
    }

    [Fact]
    public void A_default_money_has_no_currency_and_refuses_every_operation()
    {
        Assert.Null(default(Money).Currency);
        Assert.Throws<InvalidOperationException>(() => default(Money).Round());
        Assert.Throws<InvalidOperationException>(() => default(Money).Add(new Money(1m, "EUR")));
        Assert.Throws<InvalidOperationException>(() => new Money(1m, "EUR").Add(default));
        Assert.Throws<InvalidOperationException>(() => Money.Sum([default]));
    }
}
