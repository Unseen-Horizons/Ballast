// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;

namespace Ballast.Auth;

/// <summary>
/// Generates a throwaway password for an account that a development environment seeds without being given one.
/// <para>
/// Each character class is guaranteed rather than left to chance, so a generated password always satisfies the library's default
/// password rules (<see cref="AuthPasswordRules"/>): at least 12 characters, with an uppercase letter, a lowercase letter, a digit and a
/// non-alphanumeric character. Use it only where a seeded credential is acceptable; an environment that goes live should set its
/// own password instead of writing a generated one to disk.
/// </para>
/// </summary>
public static class SeedPasswordGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*";
    private const string All = Upper + Lower + Digits + Symbols;

    /// <summary>
    /// Returns a password of the given length that holds one uppercase letter, one lowercase letter, one digit and one symbol, drawn
    /// from a cryptographic random source.
    /// <para>
    /// Drawing every character uniformly from the combined alphabet does not guarantee each class appears. A missing class makes the
    /// password fail the policy, so seeding one character from each class removes that possibility rather than making it unlikely.
    /// </para>
    /// </summary>
    /// <param name="length">The total length. A value below 12 is raised to 12, the policy minimum.</param>
    /// <returns>The password.</returns>
    public static string Generate(int length)
    {
        if (length < 12)
        {
            length = 12;
        }

        var chars = new char[length];

        // One guaranteed character per required class, then fill the rest freely.
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        for (var i = 4; i < length; i++)
        {
            chars[i] = All[RandomNumberGenerator.GetInt32(All.Length)];
        }

        // Shuffled, so the guaranteed characters are not always in the first four positions. A fixed layout would narrow the search for anyone who knows how the password is made.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
