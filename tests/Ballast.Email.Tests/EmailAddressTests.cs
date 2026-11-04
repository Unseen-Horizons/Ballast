// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace Ballast.Email.Tests;

/// <summary>The shape check of an address, and the masked form a log may hold. Ported from the app's address rules.</summary>
public class EmailAddressTests
{
    [Theory]
    [InlineData("name@example.com")]
    [InlineData("ime.prezime+test@primjer.example.hr")]
    [InlineData("a@b.c")]
    [InlineData("UPPER@Example.COM")]
    [InlineData("o'brien@example.ie")]
    [InlineData("name@mail.sub.example.co.uk")]
    public void IsPlausible_PlainAddress_IsAccepted(string address)
    {
        Assert.True(EmailAddress.IsPlausible(address));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plainaddress")]
    [InlineData("name@domain")]
    [InlineData("name@@domain.hr")]
    [InlineData("@domain.hr")]
    [InlineData("name@")]
    [InlineData("na me@domain.hr")]
    [InlineData("name@domain.")]
    [InlineData("name@.hr")]
    [InlineData("name@dom..hr")]
    [InlineData("name@[127.0.0.1]")]
    [InlineData("Ime Prezime <name@domain.hr>")]
    [InlineData("<name@domain.hr>")]
    [InlineData("name@domain.hr, other@domain.hr")]
    [InlineData(" name@domain.hr")]
    [InlineData("name@domain.hr ")]
    [InlineData("name@domain.hr\n")]
    public void IsPlausible_AnythingElse_IsRejected(string? address)
    {
        Assert.False(EmailAddress.IsPlausible(address));
    }

    [Fact]
    public void IsPlausible_AtTheDefaultCap_IsAccepted_AndOneLonger_IsNot()
    {
        var atLimit = Address(EmailAddress.DefaultMaxLength);
        Assert.Equal(255, atLimit.Length);

        Assert.True(EmailAddress.IsPlausible(atLimit));
        Assert.False(EmailAddress.IsPlausible(Address(EmailAddress.DefaultMaxLength + 1)));
    }

    [Fact]
    public void IsPlausible_WithAnotherCap_UsesIt()
    {
        Assert.True(EmailAddress.IsPlausible("name@domain.hr", maxLength: 14));
        Assert.False(EmailAddress.IsPlausible("name@domain.hr", maxLength: 13));
    }

    [Fact]
    public void IsPlausible_WithACapBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailAddress.IsPlausible("name@domain.hr", maxLength: 0));
    }

    [Theory]
    [InlineData("klijent@example.test", "k***@example.test")]
    [InlineData("a@b.hr", "a***@b.hr")]
    [InlineData("@example.test", "***@example.test")]
    [InlineData("no-at-sign", "***")]
    public void Mask_ShowsTheFirstCharacterAndTheDomain(string address, string masked)
    {
        Assert.Equal(masked, EmailAddress.Mask(address));
    }

    [Fact]
    public void Mask_NeverHoldsTheWholeName()
    {
        var masked = EmailAddress.Mask("klijent.prezime@example.test");

        Assert.DoesNotContain("klijent", masked);
        Assert.DoesNotContain("prezime", masked);
        Assert.DoesNotContain("klijent.prezime@", masked);
    }

    [Fact]
    public void Mask_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EmailAddress.Mask(null!));
    }

    /// <summary>"ime.prezime@" + labels + ".hr", exactly <paramref name="length"/> characters, every label at most 60 characters.</summary>
    private static string Address(int length)
    {
        const string local = "ime.prezime";
        const string suffix = ".hr";
        var domain = new StringBuilder();
        var remaining = length - local.Length - 1 - suffix.Length;
        while (remaining > 0)
        {
            var label = remaining > 61 ? 60 : remaining;
            domain.Append('b', label);
            remaining -= label;
            if (remaining > 0)
            {
                domain.Append('.');
                remaining--;
            }
        }

        return $"{local}@{domain}{suffix}";
    }
}
