// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth.Tests;

/// <summary>The seed password: its length, its character classes, and that it always meets the library's default password rules.</summary>
public class SeedPasswordGeneratorTests
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";

    [Fact]
    public void The_password_has_the_length_asked_for()
    {
        Assert.Equal(24, SeedPasswordGenerator.Generate(24).Length);
        Assert.Equal(12, SeedPasswordGenerator.Generate(12).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-3)]
    public void A_length_below_the_policy_minimum_is_raised_to_twelve(int length)
    {
        Assert.Equal(12, SeedPasswordGenerator.Generate(length).Length);
    }

    [Fact]
    public void Every_password_meets_the_default_password_rules()
    {
        var rules = new AuthPasswordRules();

        for (var i = 0; i < 500; i++)
        {
            var password = SeedPasswordGenerator.Generate(rules.RequiredLength);

            Assert.True(password.Length >= rules.RequiredLength);
            Assert.Contains(password, c => char.IsUpper(c));
            Assert.Contains(password, c => char.IsLower(c));
            Assert.Contains(password, c => char.IsDigit(c));
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
            Assert.True(password.Distinct().Count() >= rules.RequiredUniqueChars);
        }
    }

    [Fact]
    public void Every_character_comes_from_the_documented_alphabet()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.All(SeedPasswordGenerator.Generate(32), c => Assert.Contains(c, Alphabet));
        }
    }

    [Fact]
    public void Successive_passwords_differ()
    {
        var passwords = Enumerable.Range(0, 200).Select(_ => SeedPasswordGenerator.Generate(24)).ToList();

        Assert.Equal(passwords.Count, passwords.Distinct().Count());
    }

    [Fact]
    public void The_guaranteed_uppercase_letter_is_not_always_in_the_same_place()
    {
        var positions = Enumerable.Range(0, 200)
            .Select(_ => SeedPasswordGenerator.Generate(24))
            .Select(password => Array.FindIndex(password.ToCharArray(), c => char.IsUpper(c)))
            .Distinct()
            .ToList();

        Assert.True(positions.Count > 1, "The shuffle must move the guaranteed characters.");
    }
}
