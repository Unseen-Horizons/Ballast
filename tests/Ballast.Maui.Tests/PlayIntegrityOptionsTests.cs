// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.AppAttestation;

namespace Ballast.Maui.Tests;

/// <summary>The optional Google Cloud project number: used only when it is a positive whole number, and left out otherwise.</summary>
public class PlayIntegrityOptionsTests
{
    [Theory]
    [InlineData("123456789012", 123456789012L)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    public void A_project_number_is_used_only_when_it_is_a_positive_whole_number(string? text, long? expected) =>
        Assert.Equal(expected, PlayIntegrityOptions.ParseProjectNumber(text));
}
