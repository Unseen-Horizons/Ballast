// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AppUpdates;
using Ballast.Maui.Platform;

namespace Ballast.Maui.Tests;

/// <summary>The install channel as a pure function: which installer package is which channel, and which channels are stores.</summary>
public sealed class InstallChannelTests
{
    [Theory]
    [InlineData("com.android.vending", InstallChannels.Play)]
    [InlineData(" com.android.vending ", InstallChannels.Play)]
    [InlineData("com.amazon.venezia", InstallChannels.Direct)]
    [InlineData("com.google.android.packageinstaller", InstallChannels.Direct)]
    [InlineData("com.android.vending.fake", InstallChannels.Direct)]
    [InlineData("COM.ANDROID.VENDING", InstallChannels.Direct)]
    [InlineData("", InstallChannels.Direct)]
    [InlineData("   ", InstallChannels.Direct)]
    [InlineData(null, InstallChannels.Direct)]
    public void An_android_installer_package_maps_to_a_channel(string? installer, string expected)
    {
        Assert.Equal(expected, InstallChannels.FromAndroidInstaller(installer));
    }

    [Fact]
    public void Only_the_store_channels_are_stores()
    {
        Assert.True(InstallChannels.IsStore(InstallChannels.Play));
        Assert.True(InstallChannels.IsStore(InstallChannels.AppStore));
        Assert.False(InstallChannels.IsStore(InstallChannels.Direct));
        Assert.False(InstallChannels.IsStore(null));
    }

    [Fact]
    public void A_fixed_channel_reports_what_it_was_given()
    {
        Assert.Equal(InstallChannels.AppStore, new FixedInstallChannel(InstallChannels.AppStore).Channel);
    }
}
