// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Ballast.Maui.Tests;

/// <summary>The app's HTTP clients: one base address and timeout rule, the handler lifetime, and the certificate option refused at start-up when the build is not debuggable.</summary>
public sealed class MauiHttpClientsTests
{
    private static readonly Uri Base = new("https://example.test/");

    [Fact]
    public void A_client_gets_its_base_address_and_timeout()
    {
        using var client = MauiHttpClients.Create(Base, TimeSpan.FromSeconds(20), new MauiHttpOptions(), new FakeRuntimeBuild(false));

        Assert.Equal(Base, client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(20), client.Timeout);
    }

    [Fact]
    public void A_zero_timeout_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MauiHttpClients.Create(Base, TimeSpan.Zero, new MauiHttpOptions(), new FakeRuntimeBuild(false)));
    }

    [Fact]
    public void A_client_with_the_certificate_option_is_refused_in_a_build_that_is_not_debuggable()
    {
        var options = new MauiHttpOptions { AllowDangerousCertificates = true };

        Assert.Throws<InvalidOperationException>(() => MauiHttpClients.Create(Base, TimeSpan.FromSeconds(20), options, new FakeRuntimeBuild(false)));
    }

    [Fact]
    public void A_named_client_is_refused_at_registration_when_the_certificate_option_is_not_allowed()
    {
        var options = new MauiHttpOptions { AllowDangerousCertificates = true };

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNamedClient("api", _ => { }, options, new FakeRuntimeBuild(false)));
    }

    [Fact]
    public void A_named_client_is_made_with_its_base_address_and_handler_lifetime()
    {
        var services = new ServiceCollection();
        services.AddNamedClient(
            "api",
            client => client.BaseAddress = Base,
            new MauiHttpOptions { HandlerLifetime = TimeSpan.FromMinutes(3) },
            new FakeRuntimeBuild(false));
        using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
        var lifetime = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get("api").HandlerLifetime;

        Assert.Equal(Base, client.BaseAddress);
        Assert.Equal(TimeSpan.FromMinutes(3), lifetime);
    }
}
