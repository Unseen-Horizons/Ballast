// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Permissions;

namespace Ballast.Maui.Tests;

/// <summary>Which location question the locate button asks: the system prompt while the platform will show it, and the settings dialog once it will not.</summary>
public sealed class LocationAskPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Allowed_location_asks_nothing_whatever_the_rest_says(bool showRationale, bool requested)
    {
        Assert.Equal(LocationAsk.AlreadyAllowed, LocationAskPolicy.Decide(true, showRationale, requested));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void After_one_refusal_the_system_prompt_is_shown_again(bool requested)
    {
        Assert.Equal(LocationAsk.SystemPrompt, LocationAskPolicy.Decide(false, true, requested));
    }

    [Fact]
    public void A_restored_install_that_never_asked_gets_the_system_prompt()
    {
        // Not allowed (a reinstall never restores the grant), no rationale, and not requested on this install.
        Assert.Equal(LocationAsk.SystemPrompt, LocationAskPolicy.Decide(false, false, false));
    }

    [Fact]
    public void Refused_twice_on_this_install_opens_settings()
    {
        // Not allowed, no rationale (the platform says no more), and this install has asked before.
        Assert.Equal(LocationAsk.OpenSettings, LocationAskPolicy.Decide(false, false, true));
    }
}
