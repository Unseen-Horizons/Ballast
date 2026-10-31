// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Money.Tests;

public class CurrenciesTests
{
    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    [InlineData("GBP", 2)]
    [InlineData("CHF", 2)]
    [InlineData("BAM", 2)]
    [InlineData("RSD", 2)]
    [InlineData("MKD", 2)]
    [InlineData("ALL", 2)]
    [InlineData("HUF", 2)]
    [InlineData("RON", 2)]
    [InlineData("CZK", 2)]
    [InlineData("PLN", 2)]
    [InlineData("SEK", 2)]
    [InlineData("BRL", 2)]
    [InlineData("HRK", 2)]
    [InlineData("BGN", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("ISK", 0)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    [InlineData("TND", 3)]
    [InlineData("CLF", 4)]
    public void Each_listed_currency_has_its_minor_units(string code, int expected)
    {
        Assert.Equal<int?>(expected, Currencies.MinorUnits(code));
    }

    [Theory]
    [InlineData("eur", 2)]
    [InlineData("jpy", 0)]
    [InlineData("kwd", 3)]
    public void The_code_is_matched_in_any_case(string code, int expected)
    {
        Assert.Equal<int?>(expected, Currencies.MinorUnits(code));
    }

    [Theory]
    [InlineData("XAU")]
    [InlineData("XDR")]
    [InlineData("XXX")]
    [InlineData("XYZ")]
    [InlineData("EURO")]
    [InlineData("")]
    [InlineData(null)]
    public void A_code_outside_the_table_has_no_known_minor_units_and_is_never_assumed_to_have_two(string? code)
    {
        Assert.Null(Currencies.MinorUnits(code));
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("eur")]
    [InlineData("XYZ")]
    public void Three_ASCII_letters_have_the_shape_of_a_code(string code)
    {
        // The shape is right even for a code the library does not know: IsIsoCode checks shape, MinorUnits knows the table.
        Assert.True(Currencies.IsIsoCode(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" EU")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("ÉUR")]
    [InlineData(null)]
    public void Anything_else_does_not_have_the_shape_of_a_code(string? code)
    {
        Assert.False(Currencies.IsIsoCode(code));
    }
}
