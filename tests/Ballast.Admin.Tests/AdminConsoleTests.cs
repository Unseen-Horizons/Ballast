// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AspNetCore;

namespace Ballast.Admin.Tests;

public class AdminConsoleTests
{
    private const string DefaultPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    [Fact]
    public void The_default_policy_allows_no_inline_script_and_no_outside_origin()
    {
        Assert.Equal(DefaultPolicy, AdminConsole.ContentSecurityPolicy(new AdminConsoleOptions()));
    }

    [Fact]
    public void Google_fonts_sources_give_the_policy_of_the_app_that_serves_its_admin_with_them()
    {
        var options = new AdminConsoleOptions();
        options.StyleSources.Add("https://fonts.googleapis.com");
        options.FontSources.Add("https://fonts.gstatic.com");

        Assert.Equal(
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; img-src 'self' data:; connect-src 'self'; font-src 'self' https://fonts.gstatic.com; frame-ancestors 'none'; base-uri 'self'; form-action 'self'",
            AdminConsole.ContentSecurityPolicy(options));
    }

    [Fact]
    public void Turning_inline_styles_off_removes_unsafe_inline_from_style_src()
    {
        var options = new AdminConsoleOptions { AllowInlineStyles = false };

        Assert.Contains("style-src 'self'; ", AdminConsole.ContentSecurityPolicy(options));
        Assert.DoesNotContain("unsafe-inline", AdminConsole.ContentSecurityPolicy(options));
    }

    [Fact]
    public void Extra_sources_follow_self_in_the_order_they_were_added()
    {
        var options = new AdminConsoleOptions();
        options.ImageSources.Add("https://tiles.example.test");
        options.ImageSources.Add("blob:");
        options.ConnectSources.Add("https://api.example.test");

        var policy = AdminConsole.ContentSecurityPolicy(options);

        Assert.Contains("img-src 'self' data: https://tiles.example.test blob:;", policy);
        Assert.Contains("connect-src 'self' https://api.example.test;", policy);
    }

    [Fact]
    public void No_policy_ever_allows_inline_script_or_a_wildcard_or_framing()
    {
        var options = new AdminConsoleOptions();
        options.StyleSources.Add("https://fonts.googleapis.com");

        var policy = AdminConsole.ContentSecurityPolicy(options);

        Assert.Contains("script-src 'self';", policy);
        Assert.DoesNotContain("'unsafe-eval'", policy);
        Assert.DoesNotContain("script-src 'unsafe-inline'", policy);
        Assert.DoesNotContain(" * ", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://a.example.test b.example.test")]
    [InlineData("https://a.example.test;script-src *")]
    [InlineData("https://a.example.test,https://b.example.test")]
    [InlineData("*")]
    public void A_source_that_is_blank_has_white_space_or_could_end_a_directive_is_refused(string source)
    {
        var options = new AdminConsoleOptions();
        options.StyleSources.Add(source);

        var ex = Assert.Throws<ArgumentException>(() => AdminConsole.ContentSecurityPolicy(options));
        Assert.Equal(nameof(AdminConsoleOptions.StyleSources), ex.ParamName);
    }

    [Theory]
    [InlineData(nameof(AdminConsoleOptions.StyleSources))]
    [InlineData(nameof(AdminConsoleOptions.FontSources))]
    [InlineData(nameof(AdminConsoleOptions.ImageSources))]
    [InlineData(nameof(AdminConsoleOptions.ConnectSources))]
    public void Every_source_list_refuses_a_wildcard(string listName)
    {
        var options = new AdminConsoleOptions();
        var list = listName switch
        {
            nameof(AdminConsoleOptions.StyleSources) => options.StyleSources,
            nameof(AdminConsoleOptions.FontSources) => options.FontSources,
            nameof(AdminConsoleOptions.ImageSources) => options.ImageSources,
            _ => options.ConnectSources,
        };
        list.Add("*");

        var ex = Assert.Throws<ArgumentException>(() => AdminConsole.ContentSecurityPolicy(options));
        Assert.Equal(listName, ex.ParamName);
    }

    [Fact]
    public void Null_options_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => AdminConsole.ContentSecurityPolicy(null!));
    }

    [Fact]
    public void The_default_rule_covers_admin_with_its_policy_cache_rule_and_headers()
    {
        var rule = AdminConsole.Rule();

        Assert.Equal("/admin", rule.Path);
        Assert.False(rule.ExactPath);
        Assert.Equal("no-cache", rule.CacheControl);
        Assert.Equal(DefaultPolicy, rule.ContentSecurityPolicy);
        Assert.Equal(3, rule.Headers.Count);
        Assert.Equal("noindex, nofollow", rule.Headers["X-Robots-Tag"]);
        Assert.Equal("nosniff", rule.Headers["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", rule.Headers["Referrer-Policy"]);
    }

    [Fact]
    public void The_rule_takes_its_path_and_cache_control_from_the_options()
    {
        var rule = AdminConsole.Rule(options =>
        {
            options.Path = "/console";
            options.CacheControl = "no-store";
        });

        Assert.Equal("/console", rule.Path);
        Assert.Equal("no-store", rule.CacheControl);
    }

    [Fact]
    public void A_blank_cache_control_is_refused()
    {
        var ex = Assert.Throws<ArgumentException>(() => AdminConsole.Rule(options => options.CacheControl = " "));
        Assert.Equal(nameof(AdminConsoleOptions.CacheControl), ex.ParamName);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData(" ")]
    public void A_path_that_is_blank_or_does_not_start_with_a_slash_is_refused(string path)
    {
        var ex = Assert.Throws<ArgumentException>(() => AdminConsole.Rule(options => options.Path = path));
        Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public void AddAdminConsole_adds_one_rule_and_returns_the_same_options()
    {
        var headers = new SecurityHeaderOptions();

        var result = headers.AddAdminConsole(options => options.Path = "/staff");

        Assert.Same(headers, result);
        var rule = Assert.Single(headers.StaticFileRules);
        Assert.Equal("/staff", rule.Path);
    }

    [Fact]
    public void AddAdminConsole_without_options_uses_the_defaults()
    {
        var headers = new SecurityHeaderOptions().AddAdminConsole();

        var rule = Assert.Single(headers.StaticFileRules);
        Assert.Equal("/admin", rule.Path);
        Assert.Equal(DefaultPolicy, rule.ContentSecurityPolicy);
    }

    [Fact]
    public void AddAdminConsole_on_null_options_is_refused()
    {
        SecurityHeaderOptions? headers = null;

        Assert.Throws<ArgumentNullException>(() => headers!.AddAdminConsole());
    }
}
