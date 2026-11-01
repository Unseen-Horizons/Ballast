// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Http.Tests;

/// <summary>
/// AddPoliteFetcher registers a keyed fetcher and the named client it reads through: the crawler's user agent with its info URL, the
/// timeout from the options, and the checks on the options that name the crawler wrongly. Ported from GetThere's registration tests.
/// </summary>
public class PoliteFetcherRegistrationTests
{
    [Fact]
    public void The_registered_client_identifies_the_crawler_and_has_the_configured_timeout()
    {
        var services = new ServiceCollection().AddPoliteFetcher("discovery", Configure("https://example.test/bot"));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("discovery");

        Assert.Equal("ExampleBot/1.0 (+https://example.test/bot)", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal(TimeSpan.FromSeconds(60), client.Timeout);
    }

    [Fact]
    public void The_timeout_and_the_version_come_from_the_options()
    {
        var services = new ServiceCollection().AddPoliteFetcher("discovery", o =>
        {
            Configure("https://example.test/bot")(o);
            o.Version = "2.5";
            o.Timeout = TimeSpan.FromSeconds(20);
        });
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("discovery");

        Assert.Equal("ExampleBot/2.5 (+https://example.test/bot)", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal(TimeSpan.FromSeconds(20), client.Timeout);
    }

    [Fact]
    public void The_fetcher_is_keyed_by_its_name_so_two_bots_do_not_share_one()
    {
        var services = new ServiceCollection()
            .AddPoliteFetcher("discovery", Configure("https://example.test/bot"))
            .AddPoliteFetcher("prices", Configure("https://example.test/bot"));
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredKeyedService<PoliteFetcher>("discovery");
        var prices = provider.GetRequiredKeyedService<PoliteFetcher>("prices");

        Assert.NotSame(discovery, prices);
        Assert.Same(discovery, provider.GetRequiredKeyedService<PoliteFetcher>("discovery"));
    }

    [Fact]
    public void A_bot_info_url_that_is_not_https_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPoliteFetcher("discovery", Configure("http://example.test/bot")));
    }

    [Fact]
    public void A_missing_bot_info_url_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPoliteFetcher("discovery", o => o.ProductToken = "ExampleBot"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Example Bot")]
    [InlineData("Example/Bot")]
    public void A_missing_or_invalid_product_token_is_refused(string token)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPoliteFetcher("discovery", o =>
        {
            o.ProductToken = token;
            o.BotInfoUrl = "https://example.test/bot";
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.test")]
    [InlineData("example.test/x")]
    [InlineData("example .test")]
    public void A_blocked_host_that_is_not_a_bare_host_name_is_refused_at_registration(string entry)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPoliteFetcher("discovery", o =>
        {
            Configure("https://example.test/bot")(o);
            o.BlockedHosts = [entry];
        }));
    }

    [Fact]
    public void A_zero_timeout_is_refused_at_registration()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPoliteFetcher("discovery", o =>
        {
            Configure("https://example.test/bot")(o);
            o.Timeout = TimeSpan.Zero;
        }));
    }

    private static Action<PoliteFetcherOptions> Configure(string botInfoUrl) => o =>
    {
        o.ProductToken = "ExampleBot";
        o.BotInfoUrl = botInfoUrl;
    };
}
