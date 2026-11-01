// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.AspNetCore.Tests.Support;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Ballast.AspNetCore.Tests.Security;

/// <summary>The security headers of every response, and the rules for the headers of static files.</summary>
public class SecurityHeaderTests
{
    private const string DefaultPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'self'";

    [Fact]
    public async Task Every_response_carries_the_fixed_security_headers_and_the_default_policy()
    {
        await using var served = await Start(_ => { }, null);

        using var response = await served.Client.GetAsync("/plain");

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("camera=(), microphone=(), geolocation=()", Header(response, "Permissions-Policy"));
        Assert.Equal(DefaultPolicy, Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task A_policy_the_endpoint_sets_itself_is_not_replaced()
    {
        await using var served = await Start(app => app.MapGet("/custom", context =>
        {
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'";
            return context.Response.WriteAsync("ok");
        }), null);

        using var response = await served.Client.GetAsync("/custom");

        Assert.Equal("default-src 'self'", Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task A_null_permissions_policy_writes_no_header()
    {
        await using var served = await Start(_ => { }, options => options.PermissionsPolicy = null);

        using var response = await served.Client.GetAsync("/plain");

        Assert.False(response.Headers.Contains("Permissions-Policy"));
    }

    [Fact]
    public async Task The_default_policy_and_the_referrer_policy_come_from_the_options()
    {
        await using var served = await Start(_ => { }, options =>
        {
            options.DefaultContentSecurityPolicy = "default-src 'self'";
            options.ReferrerPolicy = "same-origin";
        });

        using var response = await served.Client.GetAsync("/plain");

        Assert.Equal("default-src 'self'", Header(response, "Content-Security-Policy"));
        Assert.Equal("same-origin", Header(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task Static_files_get_the_rules_that_match_their_path_in_the_order_the_rules_were_added()
    {
        using var root = WebRoot.Create();
        await using var served = await Start(root, _ => { }, options => Rules(options));

        using var admin = await served.Client.GetAsync("/admin/x.js");
        using var editor = await served.Client.GetAsync("/admin/price-editor.html");
        using var images = await served.Client.GetAsync("/images/a.png");
        using var other = await served.Client.GetAsync("/other/b.png");
        using var map = await served.Client.GetAsync("/map/public.html");
        using var mapScript = await served.Client.GetAsync("/map/app.js");

        Assert.Equal("default-src 'self'", Header(admin, "Content-Security-Policy"));
        AssertCacheControl("no-cache", admin);
        Assert.Equal("noindex, nofollow", Header(admin, "X-Robots-Tag"));

        Assert.Equal("default-src 'self'; img-src 'self' blob:", Header(editor, "Content-Security-Policy"));
        AssertCacheControl("no-cache", editor);

        AssertCacheControl("public, max-age=31536000, immutable", images);
        AssertCacheControl("no-cache, no-store", other);

        AssertCacheControl("no-cache", map);
        Assert.Equal("geolocation=(self)", Header(map, "Permissions-Policy"));
        AssertCacheControl("public, max-age=3600, must-revalidate", mapScript);
    }

    [Fact]
    public async Task A_static_file_no_rule_covers_keeps_the_default_policy_and_no_cache_header()
    {
        using var root = WebRoot.Create();
        await using var served = await Start(root, _ => { }, options => Rules(options));

        using var response = await served.Client.GetAsync("/plain.txt");

        Assert.Equal(DefaultPolicy, Header(response, "Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Cache-Control"));
    }

    [Fact]
    public async Task A_rule_for_a_path_matches_by_segment_so_a_sibling_name_is_not_covered()
    {
        using var root = WebRoot.Create();
        await using var served = await Start(root, _ => { }, options => Rules(options));

        using var response = await served.Client.GetAsync("/adminx.txt");

        Assert.Equal(DefaultPolicy, Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task An_exact_rule_covers_only_its_own_path()
    {
        using var root = WebRoot.Create();
        await using var served = await Start(root, _ => { }, options => Rules(options));

        using var response = await served.Client.GetAsync("/admin/price-editor.html");
        using var sibling = await served.Client.GetAsync("/admin/x.js");

        Assert.Equal("default-src 'self'; img-src 'self' blob:", Header(response, "Content-Security-Policy"));
        Assert.Equal("default-src 'self'", Header(sibling, "Content-Security-Policy"));
    }

    [Fact]
    public async Task The_fixed_headers_are_on_static_files_too()
    {
        using var root = WebRoot.Create();
        await using var served = await Start(root, _ => { }, options => Rules(options));

        using var response = await served.Client.GetAsync("/admin/x.js");

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
    }

    [Fact]
    public void Using_the_security_headers_without_adding_them_is_refused()
    {
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => app.UseSecurityHeaders());
    }

    [Fact]
    public void Using_the_secure_static_files_without_adding_the_headers_is_refused()
    {
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => app.UseSecureStaticFiles());
    }

    [Fact]
    public void A_blank_header_value_is_refused_when_the_headers_are_added()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSecurityHeaders(options => options.ReferrerPolicy = " "));

        Assert.Contains("ReferrerPolicy", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_header_name_in_a_rule_is_refused_when_the_headers_are_added()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSecurityHeaders(options =>
        {
            var rule = new StaticFileRule("/map");
            rule.Headers[" "] = "value";
            options.StaticFileRules.Add(rule);
        }));

        Assert.Contains("/map", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("")]
    [InlineData(" ")]
    public void A_static_file_rule_path_must_start_with_a_slash(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => new StaticFileRule(path));
    }

    private static void Rules(SecurityHeaderOptions options)
    {
        // Added in this order, a later rule overwrites the header an earlier one set.
        var png = new StaticFileRule("/") { CacheControl = "no-cache, no-store" };
        png.Suffixes.Add(".png");
        options.StaticFileRules.Add(png);

        options.StaticFileRules.Add(new StaticFileRule("/images") { CacheControl = "public, max-age=31536000, immutable" });

        var admin = new StaticFileRule("/admin") { ContentSecurityPolicy = "default-src 'self'", CacheControl = "no-cache" };
        admin.Headers["X-Robots-Tag"] = "noindex, nofollow";
        options.StaticFileRules.Add(admin);

        options.StaticFileRules.Add(new StaticFileRule("/admin/price-editor.html")
        {
            ExactPath = true,
            ContentSecurityPolicy = "default-src 'self'; img-src 'self' blob:",
        });

        var map = new StaticFileRule("/map") { CacheControl = "public, max-age=3600, must-revalidate" };
        map.Headers["Permissions-Policy"] = "geolocation=(self)";
        options.StaticFileRules.Add(map);

        var mapPages = new StaticFileRule("/map") { CacheControl = "no-cache" };
        mapPages.Suffixes.Add(".html");
        options.StaticFileRules.Add(mapPages);
    }

    private static void AssertCacheControl(string expected, HttpResponseMessage response)
    {
        // The client re-orders a parsed Cache-Control value, so the directives are compared as a set.
        var actual = Header(response, "Cache-Control") ?? string.Empty;
        Assert.Equal(Directives(expected), Directives(actual));
    }

    private static string[] Directives(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal).ToArray();

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return string.Join(", ", values);
        }

        return response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(", ", contentValues) : null;
    }

    private static Task<Served> Start(Action<WebApplication> endpoints, Action<SecurityHeaderOptions>? options) =>
        Start(null, endpoints, options);

    private static Task<Served> Start(WebRoot? root, Action<WebApplication> endpoints, Action<SecurityHeaderOptions>? options) => Served.StartAsync(
        builder =>
        {
            if (root is not null)
            {
                builder.Environment.WebRootFileProvider = new PhysicalFileProvider(root.Path);
            }

            builder.Services.AddSecurityHeaders(options);
        },
        app =>
        {
            app.UseSecurityHeaders();
            app.UseSecureStaticFiles();
            endpoints(app);
            app.MapGet("/plain", () => "ok");
        });

    /// <summary>A temporary folder of files to serve, removed when disposed.</summary>
    private sealed class WebRoot : IDisposable
    {
        private WebRoot(string path) => Path = path;

        public string Path { get; }

        public static WebRoot Create()
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "web-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "admin"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "images"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "other"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "map"));
            foreach (var file in new[] { "admin/x.js", "admin/price-editor.html", "images/a.png", "other/b.png", "map/public.html", "map/app.js", "plain.txt", "adminx.txt" })
            {
                File.WriteAllText(System.IO.Path.Combine(path, file), "content");
            }

            return new WebRoot(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
