// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates.Tests;

/// <summary>
/// The install channel names as pure functions: which installer package is which channel, and which channels may consult a store. The
/// platform's own reads (the package manager) are checked on a device.
/// </summary>
public class InstallChannelsTests
{
    private sealed class CountingUpdater : IAppUpdater
    {
        public int Asked { get; private set; }

        public bool CanStart(string? storeUrl) => true;

        public Task<AppUpdateStartResult> StartAsync(string? storeUrl) => Task.FromResult(AppUpdateStartResult.Started);

        public Task<StoreUpdate?> IsUpdateAvailableAsync()
        {
            Asked++;
            return Task.FromResult<StoreUpdate?>(new StoreUpdate(true, 99));
        }
    }

    private static AppVersionResponse Info(int latest, int minimum = 0) =>
        new() { Platform = "Android", LatestBuild = latest, MinimumBuild = minimum };

    // ── Installer package to channel ─────────────────────────────────────────

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
    public void An_android_installer_package_maps_to_a_channel(string? installer, string expected) =>
        Assert.Equal(expected, InstallChannels.FromAndroidInstaller(installer));

    [Fact]
    public void Only_the_store_channels_are_stores()
    {
        Assert.True(InstallChannels.IsStore(InstallChannels.Play));
        Assert.True(InstallChannels.IsStore(InstallChannels.AppStore));
        Assert.False(InstallChannels.IsStore(InstallChannels.Direct));
        Assert.False(InstallChannels.IsStore(null));
    }

    [Fact]
    public void The_default_channel_names_are_the_wire_names()
    {
        Assert.Equal("play", InstallChannels.Play);
        Assert.Equal("direct", InstallChannels.Direct);
        Assert.Equal("appstore", InstallChannels.AppStore);
    }

    // ── Which channels may ask a store ───────────────────────────────────────

    [Theory]
    [InlineData(InstallChannels.Direct)]
    [InlineData(InstallChannels.AppStore)]
    [InlineData(null)]
    public async Task Direct_and_appstore_and_unknown_never_consult_a_store(string? channel)
    {
        var updater = new CountingUpdater();

        var store = await AppVersionRules.AskStoreAsync(AppVersionRules.StoreUpdaterFor(channel, updater), TimeSpan.FromSeconds(5));

        Assert.Null(store);
        Assert.Equal(0, updater.Asked);
        // So the server's number for that channel decides the banner.
        Assert.Equal(AppUpdateDecision.UpdateAvailable, AppVersionRules.Decide(8, Info(latest: 12), store, null));
    }

    [Fact]
    public async Task The_store_channel_asks_its_store()
    {
        var updater = new CountingUpdater();

        var store = await AppVersionRules.AskStoreAsync(AppVersionRules.StoreUpdaterFor(InstallChannels.Play, updater), TimeSpan.FromSeconds(5));

        Assert.Equal(1, updater.Asked);
        Assert.Equal(99, store!.VersionCode);
    }

    [Fact]
    public void The_store_channel_is_an_argument_so_an_app_can_name_its_own()
    {
        var updater = new CountingUpdater();

        Assert.Same(updater, AppVersionRules.StoreUpdaterFor("beta", updater, storeChannel: "beta"));
        Assert.Null(AppVersionRules.StoreUpdaterFor(InstallChannels.Play, updater, storeChannel: "beta"));
    }

    [Theory]
    [InlineData(InstallChannels.Play)]
    [InlineData(InstallChannels.Direct)]
    [InlineData(InstallChannels.AppStore)]
    public void Required_beats_a_dismissal_on_every_channel(string channel)
    {
        // The decision reads no channel: the minimum is the server's for that channel, and a dismissal never lifts it.
        var info = Info(latest: 12, minimum: 9);
        var store = channel == InstallChannels.Play ? new StoreUpdate(true, 12) : null;

        Assert.Equal(AppUpdateDecision.UpdateRequired, AppVersionRules.Decide(8, info, store, dismissedBuild: 12));
    }

    [Fact]
    public void A_minimum_only_answer_with_no_latest_build_blocks_below_it_and_announces_nothing()
    {
        var info = Info(latest: 0, minimum: 9);

        Assert.Equal(AppUpdateDecision.UpdateRequired, AppVersionRules.Decide(8, info, null, null));
        Assert.Equal(AppUpdateDecision.UpToDate, AppVersionRules.Decide(9, info, null, null));
        Assert.Equal(0, AppVersionRules.AnnouncedBuild(9, info, null));
    }

    [Fact]
    public void The_app_state_says_it_is_direct_only_for_the_direct_channel()
    {
        Assert.True(new AppUpdateState(AppUpdateDecision.UpToDate, 1, 0, null, null, null, InstallChannels.Direct).IsDirect);
        Assert.False(new AppUpdateState(AppUpdateDecision.UpToDate, 1, 0, null, null, null, InstallChannels.Play).IsDirect);
        Assert.False(AppUpdateState.None.IsDirect);
    }
}
