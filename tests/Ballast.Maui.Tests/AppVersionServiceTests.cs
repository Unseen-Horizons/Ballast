// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.AppUpdates;
using Ballast.Maui.AppUpdates;
using Ballast.Maui.Platform;
using Ballast.Testing.Fakes;

using Microsoft.Extensions.Logging;

namespace Ballast.Maui.Tests;

/// <summary>The device's update check, and what it logs when the store cannot say.</summary>
public class AppVersionServiceTests
{
    /// <summary>A store whose update question fails.</summary>
    private sealed class FailingStore : IAppUpdater
    {
        public Task<StoreUpdate?> IsUpdateAvailableAsync() =>
            Task.FromException<StoreUpdate?>(new InvalidOperationException("The store is unreachable."));

        public bool CanStart(string? storeUrl) => false;

        public Task<AppUpdateStartResult> StartAsync(string? storeUrl) => throw new NotSupportedException();
    }

    /// <summary>An Android device with a stamped build of 42 and no manifest text to compare with.</summary>
    private sealed class AndroidDevice : IUpdateDevice
    {
        public int? StampedBuild => 42;

        public string? PlatformQueryName => "android";

        public string? ManifestBuildText => null;

        public string? PlatformName => "Android";
    }

    [Fact]
    public async Task A_store_failure_is_logged_once_and_the_check_goes_on_with_the_server_answer()
    {
        var logs = new CapturedLogs();
        var handler = new ScriptedHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new AppVersionClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") });
        using var service = new AppVersionService(
            client,
            new FakePreferenceStore(),
            new FixedInstallChannel("play"),
            new AndroidDevice(),
            new FailingStore(),
            null,
            null,
            logs.CreateLogger("Ballast.Maui.AppUpdates.AppVersionService"));

        await service.CheckAsync(force: true);

        var warning = Assert.Single(logs.Records, record => record.Level == LogLevel.Warning);
        Assert.Equal("Asking the store failed; using the server's rule.", warning.Message);
        Assert.NotNull(warning.Exception);
        Assert.Single(handler.Requests);
    }
}
