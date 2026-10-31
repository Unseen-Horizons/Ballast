// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Ballast.Money;

namespace Ballast.Money.Tests;

/// <summary>Runs the example from the package README, verbatim, and checks what it prints.</summary>
public class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_runs_as_written()
    {
        var output = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(output);
        try
        {
            // ----- the README example, verbatim -----
            // Round to the currency's minor units, half away from zero.
            var price = new Money(0.125m, "eur").Round();
            Console.WriteLine(MoneyText.Format(price, CultureInfo.GetCultureInfo("en-US")));

            // A printed total always matches its printed lines.
            var lines = new[] { new Money(0.333m, "EUR"), new Money(0.333m, "EUR"), new Money(0.333m, "EUR") };
            var totals = MoneyTotals.FromLines(lines);
            Console.WriteLine(MoneyText.Format(totals.Total, CultureInfo.GetCultureInfo("en-US")));

            // The same amount with the separators of another culture.
            Console.WriteLine(MoneyText.Format(new Money(1234.5m, "EUR"), CultureInfo.GetCultureInfo("hr-HR")));
            // ----- end of the example -----
        }
        finally
        {
            Console.SetOut(previous);
        }

        var printed = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, printed.Length);
        Assert.Equal("€0.13", printed[0]);
        Assert.Equal("€0.99", printed[1]);
        Assert.Equal("€1.234,50", printed[2]);
    }
}
