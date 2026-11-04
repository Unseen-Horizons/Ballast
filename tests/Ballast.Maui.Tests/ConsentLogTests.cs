// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Ads;

namespace Ballast.Maui.Tests;

/// <summary>The consent SDK's answers as they are logged: each value as given, and a missing one said to be missing.</summary>
public sealed class ConsentLogTests
{
    [Fact]
    public void An_update_names_its_status_and_whether_ads_may_be_requested()
    {
        Assert.Equal(
            "[Ads] Consent information updated: status Required, ads may be requested: no",
            ConsentLog.InfoUpdated("Required", false));
        Assert.Equal(
            "[Ads] Consent information updated: status not given by the SDK, ads may be requested: not given",
            ConsentLog.InfoUpdated(null, null));
    }

    [Fact]
    public void A_failed_update_carries_the_SDK_message()
    {
        Assert.Equal(
            "[Ads] Consent information could not be updated: Publisher misconfiguration",
            ConsentLog.InfoFailedToUpdate("  Publisher misconfiguration "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_form_error_without_a_message_says_so(string? message)
    {
        Assert.Equal("[Ads] Consent form could not be shown: not given by the SDK", ConsentLog.FormError(message));
    }
}
