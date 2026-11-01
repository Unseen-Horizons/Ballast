// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.AspNetCore.Tests.Support;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.AspNetCore.Tests.Startup;

/// <summary>The forwarded headers are read only from a listed network, and a bad entry stops the start.</summary>
public class ForwardedHeaderTests
{
    private const string Key = "ForwardedHeaders:KnownNetworks";

    [Fact]
    public void A_malformed_network_is_refused_at_startup_and_the_message_names_its_position_and_value()
    {
        var config = Config(("0", "10.0.0.0/8"), ("1", "not-a-network"));

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddForwardedHeadersFromNetworks(config));

        Assert.Contains("ForwardedHeaders:KnownNetworks entry 1", error.Message, StringComparison.Ordinal);
        Assert.Contains("not-a-network", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_network_is_refused_at_startup()
    {
        var config = Config(("0", "10.0.0.0/8"), ("1", " "));

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddForwardedHeadersFromNetworks(config));

        Assert.Contains("entry 1 is blank", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ipv6_network_in_cidr_form_is_accepted()
    {
        var config = Config(("0", "fd00::/8"));

        var services = new ServiceCollection();
        services.AddForwardedHeadersFromNetworks(config);

        Assert.NotNull(services);
    }

    [Fact]
    public async Task A_forwarded_address_from_a_listed_network_is_believed()
    {
        await using var served = await Start(Config(("0", "10.0.0.0/8")), null, remote: "10.1.2.3", forwarded: "203.0.113.9");

        Assert.Equal("203.0.113.9", await served.Client.GetStringAsync("/remote"));
    }

    [Fact]
    public async Task A_forwarded_address_from_an_unlisted_sender_is_ignored()
    {
        await using var served = await Start(Config(("0", "10.0.0.0/8")), null, remote: "192.0.2.1", forwarded: "203.0.113.9");

        Assert.Equal("192.0.2.1", await served.Client.GetStringAsync("/remote"));
    }

    [Fact]
    public async Task With_an_empty_list_no_forwarded_address_is_read_by_default()
    {
        await using var served = await Start(new ConfigurationBuilder().Build(), null, remote: "192.0.2.1", forwarded: "203.0.113.9");

        Assert.Equal("192.0.2.1", await served.Client.GetStringAsync("/remote"));
    }

    [Fact]
    public async Task With_an_empty_list_and_the_opt_in_the_forwarded_address_is_read_from_any_sender()
    {
        await using var served = await Start(new ConfigurationBuilder().Build(), options => options.TrustAnySenderWhenEmpty = true, remote: "192.0.2.1", forwarded: "203.0.113.9");

        Assert.Equal("203.0.113.9", await served.Client.GetStringAsync("/remote"));
    }

    [Fact]
    public async Task The_key_can_be_set_to_another_configuration_section()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Proxies:Trusted:0"] = "10.0.0.0/8" }).Build();

        await using var served = await Start(config, options => options.ConfigurationKey = "Proxies:Trusted", remote: "10.1.2.3", forwarded: "203.0.113.9");

        Assert.Equal("203.0.113.9", await served.Client.GetStringAsync("/remote"));
    }

    private static IConfiguration Config(params (string Index, string Value)[] entries)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (index, value) in entries)
        {
            values[Key + ":" + index] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static async Task<Served> Start(IConfiguration config, Action<ForwardedNetworkOptions>? options, string remote, string forwarded)
    {
        var served = await Served.StartAsync(
        builder =>
        {
            builder.Configuration.AddConfiguration(config);
            builder.Services.AddForwardedHeadersFromNetworks(builder.Configuration, options);
        },
        app =>
        {
            app.Use(async (context, next) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
                await next();
            });
            app.UseForwardedHeaders();
            app.MapGet("/remote", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "none");
        });

        served.Client.DefaultRequestHeaders.Add("X-Forwarded-For", forwarded);
        return served;
    }
}
