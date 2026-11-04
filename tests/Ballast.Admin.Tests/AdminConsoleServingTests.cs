// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Admin;
using Ballast.AspNetCore;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ballast.Admin.Tests;

/// <summary>
/// The console's headers on real responses: the pipeline, the static files and the rule's path, end to end in memory.
/// </summary>
public class AdminConsoleServingTests
{
    private const string DefaultPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    private const string OtherPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'self'";

    [Fact]
    public async Task A_console_file_is_served_with_the_console_policy_cache_rule_and_headers()
    {
        await using var site = await Site.StartAsync(options => options.AddAdminConsole());

        using var response = await site.Client.GetAsync("/admin/index.html");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DefaultPolicy, Header(response, "Content-Security-Policy"));
        Assert.Equal("no-cache", Header(response, "Cache-Control"));
        Assert.Equal("noindex, nofollow", Header(response, "X-Robots-Tag"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task A_file_outside_the_console_keeps_the_default_policy_and_gets_no_console_headers()
    {
        await using var site = await Site.StartAsync(options => options.AddAdminConsole());

        using var response = await site.Client.GetAsync("/other.html");

        Assert.Equal(OtherPolicy, Header(response, "Content-Security-Policy"));
        Assert.Null(Header(response, "X-Robots-Tag"));
        Assert.Null(Header(response, "Cache-Control"));
    }

    [Fact]
    public async Task A_path_that_only_starts_with_the_console_name_is_not_the_console()
    {
        await using var site = await Site.StartAsync(options => options.AddAdminConsole());

        using var response = await site.Client.GetAsync("/adminx.html");

        Assert.Equal(OtherPolicy, Header(response, "Content-Security-Policy"));
        Assert.Null(Header(response, "X-Robots-Tag"));
    }

    [Fact]
    public async Task A_custom_path_serves_its_own_files_with_the_policy()
    {
        await using var site = await Site.StartAsync(options => options.AddAdminConsole(console => console.Path = "/admin/tools"));

        using var toolResponse = await site.Client.GetAsync("/admin/tools/index.html");
        using var consoleResponse = await site.Client.GetAsync("/admin/index.html");

        Assert.Equal(DefaultPolicy, Header(toolResponse, "Content-Security-Policy"));
        Assert.Equal(OtherPolicy, Header(consoleResponse, "Content-Security-Policy"));
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return string.Join(", ", values);
        }

        return response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(", ", contentValues) : null;
    }

    /// <summary>An in-memory site with a temporary web root, removed when disposed.</summary>
    private sealed class Site : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _root;

        private Site(WebApplication app, HttpClient client, string root)
        {
            _app = app;
            Client = client;
            _root = root;
        }

        public HttpClient Client { get; }

        public static async Task<Site> StartAsync(Action<SecurityHeaderOptions> headers)
        {
            var root = Path.Combine(AppContext.BaseDirectory, "admin-web-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "admin"));
            Directory.CreateDirectory(Path.Combine(root, "admin", "tools"));
            File.WriteAllText(Path.Combine(root, "admin", "index.html"), "<!doctype html>");
            File.WriteAllText(Path.Combine(root, "admin", "tools", "index.html"), "<!doctype html>");
            File.WriteAllText(Path.Combine(root, "other.html"), "<!doctype html>");
            File.WriteAllText(Path.Combine(root, "adminx.html"), "<!doctype html>");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Environment.WebRootFileProvider = new PhysicalFileProvider(root);
            builder.Services.AddSecurityHeaders(headers);

            var app = builder.Build();
            app.UseSecurityHeaders();
            app.UseSecureStaticFiles();
            await app.StartAsync();

            return new Site(app, app.GetTestClient(), root);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
