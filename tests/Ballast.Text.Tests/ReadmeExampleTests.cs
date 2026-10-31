// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Text;

namespace Ballast.Text.Tests;

/// <summary>
/// Runs the example in src/Ballast.Text/README.md as the README gives it, and checks the values its comments state.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_compiles_and_runs()
    {
        Console.WriteLine(TextFold.Fold("Trg bana Jelačića"));  // trg bana jelacica
        Console.WriteLine(TextFold.Fold("Đakovo", DStroke.Dj)); // djakovo

        var note = "Nazovite +385 91 123 4567 ili ana@example.com, 26.10.2026.";
        Console.WriteLine(PersonalDataMasker.Mask(note));       // Nazovite [phone] ili [email], 26.10.2026.
        Console.WriteLine(PersonalDataMasker.ContainsPersonalData(note)); // True
    }

    [Fact]
    public void The_values_the_readme_comments_state_are_the_ones_the_library_gives()
    {
        Assert.Equal("trg bana jelacica", TextFold.Fold("Trg bana Jelačića"));
        Assert.Equal("djakovo", TextFold.Fold("Đakovo", DStroke.Dj));

        var note = "Nazovite +385 91 123 4567 ili ana@example.com, 26.10.2026.";
        Assert.Equal("Nazovite [phone] ili [email], 26.10.2026.", PersonalDataMasker.Mask(note));
        Assert.True(PersonalDataMasker.ContainsPersonalData(note));
    }
}
