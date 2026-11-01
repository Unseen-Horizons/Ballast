// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Ballast.Health.Tests;

/// <summary>The build body under the web JSON defaults the minimal API uses.</summary>
public class HealthBodyTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_body_carries_status_timestamp_commit_build_time_and_start()
    {
        var body = HealthBody.Create(
            new BuildInfo("0c40e420a1b2c3d4e5f60718293a4b5c6d7e8f90", "2026-10-21T10:00:00Z"),
            new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 21, 10, 5, 0, DateTimeKind.Utc));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(body, Web));
        var root = json.RootElement;

        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.Equal("2026-10-21T10:05:00Z", root.GetProperty("timestamp").GetString());
        Assert.Equal("0c40e420a1b2c3d4e5f60718293a4b5c6d7e8f90", root.GetProperty("commit").GetString());
        Assert.Equal("2026-10-21T10:00:00Z", root.GetProperty("builtAtUtc").GetString());
        Assert.Equal("2026-10-21T09:00:00Z", root.GetProperty("startedUtc").GetString());
    }

    [Fact]
    public void An_unrecorded_build_is_present_in_the_body_as_null()
    {
        var body = HealthBody.Create(new BuildInfo(null, null), DateTime.UtcNow, DateTime.UtcNow);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(body, Web));
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("commit", out var commit) && commit.ValueKind == JsonValueKind.Null);
        Assert.True(root.TryGetProperty("builtAtUtc", out var builtAt) && builtAt.ValueKind == JsonValueKind.Null);
        Assert.True(root.TryGetProperty("startedUtc", out _));
    }
}
