// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.Testing.Hosting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballast.Testing.Tests.Hosting;

public class InMemoryAppTests
{
    [Fact]
    public async Task An_in_memory_app_serves_its_routes_and_captures_what_it_logs()
    {
        await using var app = await InMemoryApp.StartAsync(
            builder => builder.Services.AddLogging(),
            web =>
            {
                web.MapGet("/hello", (ILogger<InMemoryAppTests> logger) =>
                {
                    logger.LogInformation("hello was asked for");
                    return "world";
                });
            },
            environmentName: "Development");

        var response = await app.Client.GetAsync("/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("world", await response.Content.ReadAsStringAsync());
        var entry = Assert.Single(app.Logs.Records, record => record.Message == "hello was asked for");
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    [Fact]
    public async Task The_environment_defaults_to_production()
    {
        await using var app = await InMemoryApp.StartAsync(
            _ => { },
            web => web.MapGet("/env", (IWebHostEnvironment environment) => environment.EnvironmentName));

        Assert.Equal("Production", await app.Client.GetStringAsync("/env"));
    }

    [Fact]
    public async Task A_null_configure_or_pipeline_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => InMemoryApp.StartAsync(null!, _ => { }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => InMemoryApp.StartAsync(_ => { }, null!));
    }
}
