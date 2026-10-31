// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Money.Tests;

public class MoneyTotalsTests
{
    [Fact]
    public void Each_line_is_rounded_and_the_total_is_the_sum_of_the_rounded_lines()
    {
        var totals = MoneyTotals.FromLines([new Money(0.333m, "EUR"), new Money(0.333m, "EUR"), new Money(0.333m, "EUR")]);

        Assert.Equal(3, totals.Lines.Count);
        Assert.All(totals.Lines, line => Assert.Equal(new Money(0.33m, "EUR"), line));
        Assert.Equal(new Money(0.99m, "EUR"), totals.Total);
    }

    [Fact]
    public void The_total_is_not_the_rounding_of_the_unrounded_sum()
    {
        // The unrounded lines add to 0.999, which rounds to 1.00. The printed lines add to 0.99, so the total is 0.99.
        var lines = new[] { new Money(0.333m, "EUR"), new Money(0.333m, "EUR"), new Money(0.333m, "EUR") };
        var unroundedTotal = Money.Sum(lines).Round();

        Assert.Equal(new Money(1.00m, "EUR"), unroundedTotal);
        Assert.NotEqual(unroundedTotal, MoneyTotals.FromLines(lines).Total);
    }

    [Fact]
    public void Two_half_cents_round_up_on_each_line_so_the_total_matches_the_printed_lines()
    {
        var lines = new[] { new Money(10.005m, "EUR"), new Money(10.005m, "EUR") };
        var totals = MoneyTotals.FromLines(lines);

        Assert.Equal(new Money(10.01m, "EUR"), totals.Lines[0]);
        Assert.Equal(new Money(20.02m, "EUR"), totals.Total);
        Assert.Equal(new Money(20.01m, "EUR"), Money.Sum(lines).Round());
    }

    [Fact]
    public void Lines_keep_the_order_they_were_given_in()
    {
        var totals = MoneyTotals.FromLines([new Money(0.125m, "EUR"), new Money(2m, "EUR")]);

        Assert.Equal(new Money(0.13m, "EUR"), totals.Lines[0]);
        Assert.Equal(new Money(2m, "EUR"), totals.Lines[1]);
        Assert.Equal("EUR", totals.Total.Currency);
    }

    [Fact]
    public void Whole_yen_lines_are_rounded_and_summed()
    {
        var totals = MoneyTotals.FromLines([new Money(100.5m, "JPY"), new Money(100.5m, "JPY")]);

        Assert.Equal(new Money(202m, "JPY"), totals.Total);
    }

    [Fact]
    public void Lines_in_two_currencies_throw_and_nothing_is_converted()
    {
        Assert.Throws<InvalidOperationException>(() => MoneyTotals.FromLines([new Money(1m, "EUR"), new Money(1m, "USD")]));
    }

    [Fact]
    public void A_line_in_a_currency_with_no_known_rounding_throws()
    {
        Assert.Throws<InvalidOperationException>(() => MoneyTotals.FromLines([new Money(1m, "XYZ")]));
    }

    [Fact]
    public void No_lines_is_refused_because_there_is_no_currency()
    {
        Assert.Throws<ArgumentException>(() => MoneyTotals.FromLines([]));
    }

    [Fact]
    public void Null_lines_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => MoneyTotals.FromLines(null!));
    }
}
