// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Text;

namespace Ballast.Text.Tests;

/// <summary>
/// The masker removes emails, phone numbers and link paths from a text before it travels onward. It leaves letters in any
/// script alone, keeps line numbers, times, dates and lists of line numbers, and never throws.
/// </summary>
public class PersonalDataMaskerTests
{
    [Fact]
    public void An_email_address_becomes_the_email_placeholder()
    {
        Assert.Equal("Napišite na [email].", PersonalDataMasker.Mask("Napišite na ana.kovac@example.com."));
    }

    [Fact]
    public void A_phone_number_with_spaces_and_dashes_becomes_the_phone_placeholder()
    {
        Assert.Equal("Nazovite [phone] sutra", PersonalDataMasker.Mask("Nazovite 091 123 4567 sutra"));
        Assert.Equal("Tel. [phone]", PersonalDataMasker.Mask("Tel. 01/234-5678"));
    }

    [Theory]
    [InlineData("+385 91 234 5678")]
    [InlineData("+385 1 234 5678")]
    [InlineData("091 234 5678")]
    [InlineData("091-234-5678")]
    [InlineData("01/2345-678")]
    [InlineData("(01) 234 5678")]
    [InlineData("00385 1 2345 678")]
    [InlineData("0912345678")]
    [InlineData("2345678")]
    public void Each_phone_form_becomes_the_phone_placeholder(string phone)
    {
        Assert.Equal("[phone]", PersonalDataMasker.Mask(phone));
    }

    [Fact]
    public void A_run_of_six_digits_is_not_a_phone_number()
    {
        Assert.Equal("123456", PersonalDataMasker.Mask("123456"));
    }

    [Theory]
    [InlineData("autobus u 14.05 i 15.10 nije došao")]
    [InlineData("07.30 08.15")]
    [InlineData("linije 101 102 103")]
    [InlineData("bus 3 4 5 6 7 8 9")]
    [InlineData("1.59 €")]
    [InlineData("cijena 12,50")]
    [InlineData("Line 6 at 14:05")]
    public void Times_line_lists_and_prices_are_kept(string text)
    {
        Assert.Equal(text, PersonalDataMasker.Mask(text));
    }

    [Fact]
    public void A_link_keeps_only_its_host_without_www()
    {
        Assert.Equal("Vidi [link: zet.hr].", PersonalDataMasker.Mask("Vidi https://www.zet.hr/vozni-red?x=1."));
    }

    [Fact]
    public void A_www_link_without_a_scheme_keeps_only_its_host()
    {
        Assert.Equal("[link: hzpp.hr]", PersonalDataMasker.Mask("www.hzpp.hr"));
    }

    [Fact]
    public void A_link_in_brackets_keeps_the_closing_bracket()
    {
        Assert.Equal("(vidi [link: zet.hr])", PersonalDataMasker.Mask("(vidi https://zet.hr)"));
    }

    [Fact]
    public void Dates_are_left_alone_including_one_with_a_time()
    {
        Assert.Equal("Od 2026-10-26 do 26.10.2026.", PersonalDataMasker.Mask("Od 2026-10-26 do 26.10.2026."));
        Assert.Equal("2026-10-26 09:00", PersonalDataMasker.Mask("2026-10-26 09:00"));
    }

    [Fact]
    public void A_day_first_date_with_dashes_is_kept_although_it_looks_like_a_phone_number()
    {
        // "01-10-2026" has eight digits and a leading 0, which the phone rule would otherwise take.
        Assert.Equal("01-10-2026", PersonalDataMasker.Mask("01-10-2026"));
    }

    [Fact]
    public void A_date_is_kept_and_a_phone_number_in_the_same_text_is_hidden()
    {
        Assert.Equal("Datum 26.10.2026, tel. [phone]", PersonalDataMasker.Mask("Datum 26.10.2026, tel. +385 91 123 4567"));
    }

    [Fact]
    public void Letters_of_any_script_pass_through_unchanged()
    {
        const string croatian = "Čakovec, Đurđevac, Šibenik, ćevapi";
        const string serbianCyrillic = "Аутобуска станица 12, Београд";
        const string hungarian = "Győr-Moson-Sopron, Szeged-Újszeged";

        Assert.Equal(croatian, PersonalDataMasker.Mask(croatian));
        Assert.Equal(serbianCyrillic, PersonalDataMasker.Mask(serbianCyrillic));
        Assert.Equal(hungarian, PersonalDataMasker.Mask(hungarian));
    }

    [Fact]
    public void Letters_around_an_email_and_a_phone_number_are_kept()
    {
        Assert.Equal("Čekam [email] i [phone]!", PersonalDataMasker.Mask("Čekam a@b.hr i 0911234567!"));
    }

    [Fact]
    public void A_file_path_is_not_masked()
    {
        const string trace = @"at Example.Map.Load() in C:\Users\alex\app\MapPage.cs:line 42";

        Assert.Equal(trace, PersonalDataMasker.Mask(trace));
    }

    [Fact]
    public void A_text_with_a_date_a_phone_number_an_email_and_a_link_masks_each_but_the_date()
    {
        const string text = "Datum 26.10.2026: +385 91 123 4567, ana@example.com, https://zet.hr";

        Assert.Equal("Datum 26.10.2026: [phone], [email], [link: zet.hr]", PersonalDataMasker.Mask(text));
    }

    [Fact]
    public void Null_or_empty_text_gives_the_empty_string()
    {
        Assert.Equal(string.Empty, PersonalDataMasker.Mask(null));
        Assert.Equal(string.Empty, PersonalDataMasker.Mask(string.Empty));
    }

    [Theory]
    [InlineData("Napišite na ana.kovac@example.com.")]
    [InlineData("Nazovite 091 123 4567 sutra")]
    [InlineData("Vidi https://www.zet.hr/vozni-red")]
    public void Contains_personal_data_is_true_for_an_email_a_phone_number_or_a_link(string text)
    {
        Assert.True(PersonalDataMasker.ContainsPersonalData(text));
    }

    [Theory]
    [InlineData("Od 2026-10-26 do 26.10.2026.")]
    [InlineData("autobus u 14.05 i 15.10 nije došao")]
    [InlineData("Čekam u redu")]
    [InlineData("[email] i [phone]")]
    [InlineData("")]
    [InlineData(null)]
    public void Contains_personal_data_is_false_when_there_is_nothing_to_hide(string? text)
    {
        Assert.False(PersonalDataMasker.ContainsPersonalData(text));
    }

    [Theory]
    [InlineData("Napišite na ana.kovac@example.com.")]
    [InlineData("Čekam a@b.hr i 0911234567!")]
    [InlineData("Od 2026-10-26 do 26.10.2026.")]
    [InlineData("Vidi https://www.zet.hr/vozni-red?x=1.")]
    public void Contains_personal_data_is_exactly_whether_mask_would_change_the_text(string text)
    {
        var changed = PersonalDataMasker.Mask(text) != text;

        Assert.Equal(changed, PersonalDataMasker.ContainsPersonalData(text));
    }
}
