// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Helpers;

namespace Ballast.Maui.Tests;

/// <summary>The packaged settings file: read once and shared, and a missing or malformed file is logged and gives no document.</summary>
public sealed class PackagedSettingsTests
{
    [Fact]
    public void The_packaged_file_is_read_and_parsed()
    {
        var package = new FakeAppPackage().With("appsettings.json", "{\"Ads\":{\"Enabled\":true}}");
        var settings = new PackagedSettings(package, "appsettings.json", new RecordingLog());

        Assert.True(settings.Document!.RootElement.GetProperty("Ads").GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public void The_file_is_read_once_and_shared_by_every_reader()
    {
        var package = new FakeAppPackage().With("appsettings.json", "{}");
        var settings = new PackagedSettings(package, "appsettings.json", new RecordingLog());

        _ = settings.Document;
        _ = settings.Document;

        Assert.Equal(1, package.Opens);
        Assert.Same(settings.Document, settings.Document);
    }

    [Fact]
    public void A_missing_file_gives_no_document_and_is_logged()
    {
        var log = new RecordingLog();
        var settings = new PackagedSettings(new FakeAppPackage(), "appsettings.json", log);

        Assert.Null(settings.Document);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void A_malformed_file_gives_no_document_and_is_logged()
    {
        var log = new RecordingLog();
        var package = new FakeAppPackage().With("appsettings.json", "{ not json");
        var settings = new PackagedSettings(package, "appsettings.json", log);

        Assert.Null(settings.Document);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void A_blank_file_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new PackagedSettings(new FakeAppPackage(), " ", new RecordingLog()));
    }
}
