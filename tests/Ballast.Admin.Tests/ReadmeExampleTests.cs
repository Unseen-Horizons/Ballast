// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;
using System.Text.Json;

using Ballast.Admin;
using Ballast.AspNetCore;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ballast.Admin.Tests;

/// <summary>
/// Runs the example in src/Ballast.Admin/README.md as it is written there. The README's using lines are at the top of this file,
/// because a test method cannot hold them. The test adds a signed-in person in front of the example, the way the app's sign-in would.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();

        // The example, copied verbatim from src/Ballast.Admin/README.md.
        builder.Services.AddSecurityHeaders(headers => headers.AddAdminConsole(console =>
        {
            console.StyleSources.Add("https://fonts.googleapis.com");
            console.FontSources.Add("https://fonts.gstatic.com");
        }));

        var app = builder.Build();

        // Test only: the app's sign-in sets the person; here a signed-in person is set directly.
        var signedIn = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", "u-1"), new Claim("name", "Ana"), new Claim("role", "Admin"), new Claim("permission", "staff.read") },
            "test"));
        app.Use(async (context, next) =>
        {
            context.User = signedIn;
            await next();
        });

        // The example, copied verbatim from src/Ballast.Admin/README.md.
        app.UseSecurityHeaders();
        app.UseSecureStaticFiles();

        app.MapGet("/admin/api/me", (ClaimsPrincipal user) =>
        {
            var me = AdminWhoAmI.Create(user, new AdminWhoAmIOptions { FullAccessRole = "Admin" });
            return me is null ? Results.Unauthorized() : Results.Ok(me);
        });

        await app.StartAsync();
        using var client = app.GetTestClient();

        // The console rule is registered with the sources the example adds.
        var rule = app.Services.GetRequiredService<SecurityHeaderOptions>().StaticFileRules.Single();
        Assert.Contains("style-src 'self' 'unsafe-inline' https://fonts.googleapis.com;", rule.ContentSecurityPolicy!);
        Assert.Contains("font-src 'self' https://fonts.gstatic.com;", rule.ContentSecurityPolicy!);

        // The endpoint answers the signed-in person with the full-access flag from the example.
        using var response = await client.GetAsync("/admin/api/me");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("u-1", json.RootElement.GetProperty("subject").GetString());
        Assert.True(json.RootElement.GetProperty("fullAccess").GetBoolean());
        Assert.Equal("staff.read", json.RootElement.GetProperty("permissions")[0].GetString());

        await app.StopAsync();
        await app.DisposeAsync();
    }
}
