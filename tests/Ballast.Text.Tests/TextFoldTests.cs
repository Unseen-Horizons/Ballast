// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Text;

namespace Ballast.Text.Tests;

public class TextFoldTests
{
    [Theory]
    [InlineData("Trg bana Jelačića", "trg bana jelacica")]
    [InlineData("Čakovec ĆIRO Žabnica Šibenik", "cakovec ciro zabnica sibenik")]
    [InlineData("Đakovo", "dakovo")]
    [InlineData("đurđevac", "durdevac")]
    [InlineData("Straße", "strasse")]
    [InlineData("Zadar", "zadar")]
    [InlineData("ZADAR", "zadar")]
    [InlineData("Zadar (gaženica)", "zadar gazenica")]
    [InlineData("Mazija / Zadar", "mazija zadar")]
    [InlineData("Zadar -- Centar!!", "zadar centar")]
    [InlineData("  Trg   bana  ", "trg bana")]
    [InlineData("Trg bana J. Jelačića", "trg bana j jelacica")]
    [InlineData("McDonald's", "mcdonalds")]
    [InlineData("Kod Mate's", "kod mates")]
    [InlineData("Kod Mate’s", "kod mates")]
    [InlineData("Kod Mate‘s", "kod mates")]
    [InlineData("Kod Mateʼs", "kod mates")]
    [InlineData("Kod Mate`s", "kod mates")]
    public void Fold_folds_diacritics_case_and_separators(string text, string expected) =>
        Assert.Equal(expected, TextFold.Fold(text));

    [Fact]
    public void Two_spellings_of_one_name_fold_to_the_same_text()
    {
        Assert.Equal(TextFold.Fold("Trg bana Jelačića"), TextFold.Fold("trg bana jelacica"));
    }

    [Fact]
    public void A_decomposed_letter_and_a_precomposed_one_are_the_same_name()
    {
        // "é" as e followed by a combining acute accent, and as the single letter.
        Assert.Equal("cafe", TextFold.Fold("Café"));
        Assert.Equal("cafe", TextFold.Fold("Café"));
    }

    [Fact]
    public void Enclosing_and_spacing_combining_marks_are_dropped_too()
    {
        // U+20DD (combining enclosing circle) is Me; U+093E (Devanagari vowel sign aa) is Mc.
        Assert.Equal("zadar", TextFold.Fold("za⃝dar"));
        Assert.Equal("ka", TextFold.Fold("kaा"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ... ")]
    public void Empty_or_punctuation_only_text_folds_to_nothing(string? text) =>
        Assert.Equal(string.Empty, TextFold.Fold(text));

    [Fact]
    public void Abbreviation_points_are_not_expanded()
    {
        Assert.Equal("j", TextFold.Fold("J."));
    }

    [Fact]
    public void The_stroke_folds_to_d_by_default()
    {
        Assert.Equal("dakovo", TextFold.Fold("Đakovo"));
        Assert.Equal("dakovo", TextFold.Fold("Đakovo", DStroke.D));
    }

    [Fact]
    public void The_stroke_folds_to_dj_when_asked()
    {
        Assert.Equal("djakovo", TextFold.Fold("Đakovo", DStroke.Dj));
        Assert.Equal("djurdjevac", TextFold.Fold("đurđevac", DStroke.Dj));
    }

    [Fact]
    public void The_capital_stroke_folds_to_lower_case_under_both_rules()
    {
        Assert.Equal("dj", TextFold.Fold("Đ", DStroke.Dj));
        Assert.Equal("d", TextFold.Fold("Đ"));
    }

    [Fact]
    public void The_capital_sharp_s_folds_to_ss()
    {
        Assert.Equal("strasse", TextFold.Fold("STRAẞE"));
    }

    [Fact]
    public void Letters_outside_the_latin_alphabet_are_kept_not_transliterated()
    {
        Assert.Equal("београд", TextFold.Fold("Београд"));
    }

    [Fact]
    public void Symbols_are_not_separators()
    {
        Assert.Equal("zadar € centar", TextFold.Fold("Zadar € centar"));
    }

    [Theory]
    [InlineData("Trg bana Jelačića")]
    [InlineData("Đakovo, Straße & McDonald's")]
    [InlineData("  ZADAR  --  centar ")]
    public void Folding_a_folded_text_changes_nothing(string text)
    {
        var once = TextFold.Fold(text);

        Assert.Equal(once, TextFold.Fold(once));
    }

    [Fact]
    public void A_stroke_value_the_enum_does_not_define_is_refused()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TextFold.Fold("Đakovo", (DStroke)7));

        Assert.Equal("dStroke", ex.ParamName);
    }

    [Fact]
    public void Text_with_an_unpaired_surrogate_is_refused_rather_than_replaced()
    {
        // Built here, not in InlineData: the test framework serialises theory arguments, which would replace a lone
        // surrogate with U+FFFD before the test runs.
        var highSurrogate = "a" + (char)0xD800 + "b";
        var lowSurrogate = "a" + (char)0xDC00 + "b";

        Assert.Throws<ArgumentException>(() => TextFold.Fold(highSurrogate));
        Assert.Throws<ArgumentException>(() => TextFold.Fold(lowSurrogate));
    }
}
