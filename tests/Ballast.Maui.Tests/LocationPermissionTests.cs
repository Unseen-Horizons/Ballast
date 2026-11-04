// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Permissions;
using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>The location permission in one place: what counts as allowed, when the prompt is shown and recorded, and what a failed call answers.</summary>
public sealed class LocationPermissionTests
{
    private static LocationPermission Create(FakePermissionSource source, RecordingLog log, PermissionPlatform platform = PermissionPlatform.Android) =>
        new(source, new InstallAnswers(new FakePreferenceStore(), "test.install", log), platform, log);

    [Fact]
    public async Task An_allowed_location_is_not_asked_for_again()
    {
        var source = new FakePermissionSource { Status = AppPermissionStatus.Granted };
        var permission = Create(source, new RecordingLog());

        Assert.True(await permission.RequestLocationAsync());
        Assert.Equal(0, source.Requests);
        Assert.False(permission.LocationRequestedOnThisInstall);
    }

    [Fact]
    public async Task A_refused_location_shows_the_prompt_once_and_records_that_it_was_shown()
    {
        var source = new FakePermissionSource { Status = AppPermissionStatus.Denied, StatusAfterRequest = AppPermissionStatus.Granted };
        var permission = Create(source, new RecordingLog());

        Assert.True(await permission.RequestLocationAsync());
        Assert.Equal(1, source.Requests);
        Assert.True(permission.LocationRequestedOnThisInstall);
    }

    [Fact]
    public async Task A_prompt_that_is_refused_again_answers_false()
    {
        var source = new FakePermissionSource { Status = AppPermissionStatus.Denied, StatusAfterRequest = AppPermissionStatus.Denied };
        var permission = Create(source, new RecordingLog());

        Assert.False(await permission.RequestLocationAsync());
        Assert.True(permission.LocationRequestedOnThisInstall);
    }

    [Fact]
    public async Task A_failed_check_is_logged_and_read_as_allowed()
    {
        var log = new RecordingLog();
        var source = new FakePermissionSource { Fault = new InvalidOperationException("no platform") };

        Assert.True(await Create(source, log).IsLocationAllowedAsync());
        Assert.Single(log.Warnings);
    }

    [Fact]
    public async Task A_failed_request_is_logged_and_answers_false()
    {
        var log = new RecordingLog();
        var source = new FakePermissionSource { Fault = new InvalidOperationException("no platform") };

        Assert.False(await Create(source, log).RequestLocationAsync());
        Assert.Single(log.Warnings);
    }

    [Fact]
    public async Task The_rationale_is_asked_of_the_platform_and_a_failed_check_is_false()
    {
        var source = new FakePermissionSource { Rationale = true };
        Assert.True(await Create(source, new RecordingLog()).ShouldShowLocationRationaleAsync());

        source.Fault = new InvalidOperationException("no platform");
        var log = new RecordingLog();

        Assert.False(await Create(source, log).ShouldShowLocationRationaleAsync());
        Assert.Single(log.Warnings);
    }

    [Theory]
    [InlineData(AppPermissionStatus.Granted, PermissionPlatform.Android, true)]
    [InlineData(AppPermissionStatus.Denied, PermissionPlatform.Android, false)]
    [InlineData(AppPermissionStatus.Unknown, PermissionPlatform.Android, false)]
    [InlineData(AppPermissionStatus.Unknown, PermissionPlatform.Ios, false)]
    [InlineData(AppPermissionStatus.Unknown, PermissionPlatform.Windows, true)]
    [InlineData(AppPermissionStatus.Granted, PermissionPlatform.Windows, true)]
    [InlineData(AppPermissionStatus.Denied, PermissionPlatform.Windows, false)]
    public void Unknown_counts_as_allowed_only_on_windows(AppPermissionStatus status, PermissionPlatform platform, bool allowed)
    {
        Assert.Equal(allowed, LocationPermission.IsAllowed(status, platform));
    }
}
