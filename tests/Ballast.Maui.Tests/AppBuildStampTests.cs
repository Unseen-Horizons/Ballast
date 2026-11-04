// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

using Ballast.Maui.AppUpdates;

namespace Ballast.Maui.Tests;

/// <summary>The build number the app stamps into its assembly: read only when it is a positive whole number, never guessed.</summary>
public class AppBuildStampTests
{
    private const string Key = "AppBuild";

    [Theory]
    [InlineData("8", 8)]
    [InlineData(" 12 ", 12)]
    public void A_stamped_build_number_is_read(string text, int expected)
    {
        Assert.True(AppBuildStamp.TryParse(text, out var build));
        Assert.Equal(expected, build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.1.0.0")]
    [InlineData("abc")]
    public void A_stamp_that_is_not_a_positive_whole_number_is_not_a_build(string? text) =>
        Assert.False(AppBuildStamp.TryParse(text, out _));

    [Fact]
    public void The_build_is_read_from_the_stamp_key_only()
    {
        var attributes = new[]
        {
            new AssemblyMetadataAttribute("Other", "77"),
            new AssemblyMetadataAttribute(Key, "8"),
        };

        Assert.Equal(8, AppBuildStamp.FromAttributes(attributes, Key));
    }

    [Fact]
    public void A_missing_or_unusable_stamp_is_null_not_a_guess()
    {
        Assert.Null(AppBuildStamp.FromAttributes([], Key));
        Assert.Null(AppBuildStamp.FromAttributes([new AssemblyMetadataAttribute(Key, "0.1.0.0")], Key));
    }
}
