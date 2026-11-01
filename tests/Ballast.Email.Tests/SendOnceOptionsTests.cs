// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email.Tests;

/// <summary>The defaults of the ledger's rules, and the combinations that are refused at startup.</summary>
public class SendOnceOptionsTests
{
    [Fact]
    public void Defaults_RetryFailedForFortyEightHours_AndNeverReclaimPending()
    {
        var options = new SendOnceOptions();

        Assert.True(options.RetryFailed);
        Assert.Equal(TimeSpan.FromHours(48), options.FailedRetryWindow);
        Assert.Null(options.PendingAfter);
        options.Validate();
    }

    [Fact]
    public void Validate_ZeroWindow_Throws_NamingTheOption()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SendOnceOptions { FailedRetryWindow = TimeSpan.Zero }.Validate());

        Assert.Equal("FailedRetryWindow", exception.ParamName);
    }

    [Fact]
    public void Validate_ZeroPendingAfter_Throws_NamingTheOption()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SendOnceOptions { RetryFailed = false, PendingAfter = TimeSpan.Zero }.Validate());

        Assert.Equal("PendingAfter", exception.ParamName);
    }

    [Fact]
    public void Validate_RetryFailedOn_PendingAfterShorterThanTheWindowPlusAnHour_Throws()
    {
        var options = new SendOnceOptions { PendingAfter = TimeSpan.FromHours(48) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

        Assert.Equal("PendingAfter", exception.ParamName);
    }

    [Fact]
    public void Validate_RetryFailedOn_PendingAfterOfTheWindowPlusAnHour_Passes()
    {
        new SendOnceOptions { PendingAfter = TimeSpan.FromHours(49) }.Validate();
    }

    [Fact]
    public void Validate_RetryFailedOff_AnyPositivePendingAfter_Passes()
    {
        new SendOnceOptions { RetryFailed = false, PendingAfter = TimeSpan.FromMinutes(5) }.Validate();
    }
}
