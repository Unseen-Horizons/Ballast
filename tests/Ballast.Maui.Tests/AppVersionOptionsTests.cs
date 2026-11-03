// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.AppUpdates;

namespace Ballast.Maui.Tests;

/// <summary>
/// The preference keys the update check stores its state under. An installed build reads what its previous build wrote, so the names are
/// fixed: changing one would forget a dismissed banner and re-check at once.
/// </summary>
public class AppVersionOptionsTests
{
    [Fact]
    public void The_stored_keys_keep_their_names_so_an_installed_build_reads_its_own_state()
    {
        var options = new AppVersionOptions();

        Assert.Equal("appversion.lastCheckUtc", options.LastCheckKey);
        Assert.Equal("appversion.dismissedLatestBuild", options.DismissedKey);
        Assert.Equal("appversion.lastResponse", options.LastResponseKey);
        Assert.Equal("appversion.lastStore", options.LastStoreKey);
    }

    [Fact]
    public void The_default_preferences_are_the_app_wide_ones_and_the_store_answer_waits_ten_seconds()
    {
        var options = new AppVersionOptions();

        Assert.Null(options.SharedPreferencesName);
        Assert.Equal(TimeSpan.FromSeconds(10), options.StoreTimeout);
    }
}
