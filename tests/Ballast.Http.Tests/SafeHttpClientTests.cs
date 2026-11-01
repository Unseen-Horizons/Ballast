// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Http.Tests;

/// <summary>
/// The named client: its User-Agent and timeout, its registration checks, and the two behaviours that matter on a real socket. The
/// connect-time check refuses a loopback address unless the development flag is set, and the client does not follow a redirect itself.
/// The loopback server is the one place these tests open a socket.
/// </summary>
public class SafeHttpClientTests
{
    [Fact]
    public void The_client_sends_the_app_user_agent_and_has_the_timeout()
    {
        var services = new ServiceCollection();
        services.AddSafeHttpClient("feeds", o =>
        {
            o.UserAgent = "ExampleApp/1.0 (+https://example.test/app)";
            o.Timeout = TimeSpan.FromSeconds(30);
        });
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("feeds");

        Assert.Equal("ExampleApp/1.0 (+https://example.test/app)", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
    }

    [Fact]
    public void The_default_timeout_is_sixty_seconds()
    {
        var services = new ServiceCollection();
        services.AddSafeHttpClient("feeds", o => o.UserAgent = "ExampleApp/1.0");
        using var provider = services.BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(60), provider.GetRequiredService<IHttpClientFactory>().CreateClient("feeds").Timeout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_user_agent_is_refused_at_registration(string agent)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSafeHttpClient("feeds", o => o.UserAgent = agent));

        Assert.Contains("UserAgent is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_agent_that_is_not_a_valid_product_is_refused_at_registration()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSafeHttpClient("feeds", o => o.UserAgent = "bad agent ((\u0001"));
    }

    [Fact]
    public void A_timeout_that_is_not_above_zero_is_refused_at_registration()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSafeHttpClient("feeds", o =>
        {
            o.UserAgent = "ExampleApp/1.0";
            o.Timeout = TimeSpan.Zero;
        }));
    }

    [Fact]
    public void A_blank_client_name_is_refused_at_registration()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSafeHttpClient(" ", o => o.UserAgent = "ExampleApp/1.0"));
    }

    [Fact]
    public async Task A_loopback_address_is_refused_at_connect_time_and_the_server_is_never_asked()
    {
        await using var server = new LoopbackServer((_, _) => (200, string.Empty, "secret"));
        var client = Client(allowNonPublic: false);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://127.0.0.1:{server.Port}/x"));

        Assert.IsType<HttpSafetyException>(ex.InnerException);
        Assert.Equal(HttpSafetyReason.NonPublicAddress, ((HttpSafetyException)ex.InnerException!).Reason);
        Assert.Empty(server.Paths);
    }

    [Fact]
    public async Task With_the_development_flag_a_loopback_address_is_reached_and_a_redirect_is_not_followed_by_the_client()
    {
        await using var server = new LoopbackServer((_, path) => path == "/start"
            ? (302, "Location: /end\r\n", string.Empty)
            : (200, string.Empty, "end"));
        var client = Client(allowNonPublic: true);

        using var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/start");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/start", Assert.Single(server.Paths));
    }

    [Fact]
    public async Task The_redirect_follower_follows_a_loopback_redirect_once_the_development_flag_is_set()
    {
        await using var server = new LoopbackServer((_, path) => path == "/start"
            ? (302, "Location: /end\r\n", string.Empty)
            : (200, string.Empty, "end"));
        var services = new ServiceCollection();
        services.AddSafeHttpClient("feeds", o =>
        {
            o.UserAgent = "ExampleApp/1.0";
            o.AllowNonPublicAddresses = true;
        });
        using var provider = services.BuildServiceProvider();
        var follower = new RedirectFollower(provider.GetRequiredService<IHttpClientFactory>(), "feeds", new RedirectFollowerOptions
        {
            RequireHttps = false,
            AllowNonPublicAddresses = true,
        });

        using var response = await follower.SendAsync(new RedirectRequest(new Uri($"http://127.0.0.1:{server.Port}/start")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("end", await response.Content.ReadAsStringAsync());
        Assert.Equal(["/start", "/end"], server.Paths);
    }

    private static HttpClient Client(bool allowNonPublic)
    {
        var services = new ServiceCollection();
        services.AddSafeHttpClient("probe", o =>
        {
            o.UserAgent = "ExampleApp/1.0";
            o.AllowNonPublicAddresses = allowNonPublic;
        });
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");
    }
}
