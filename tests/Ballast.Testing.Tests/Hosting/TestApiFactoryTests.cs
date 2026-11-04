// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Hosting;
using Ballast.Testing.Tests.Host;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ballast.Testing.Tests.Hosting;

public class TestApiFactoryTests
{
    [Fact]
    public async Task A_hosted_service_that_uses_the_database_stops_before_the_database_is_dropped()
    {
        var state = new DatabaseState();
        var factory = new ProbeFactory(state);

        await factory.InitializeAsync();
        using (var client = factory.CreateClient())
        {
            Assert.Equal("pong", await client.GetStringAsync("/ping"));
        }

        await factory.DisposeAsync();

        // The database is dropped once, after every stop of the host, and no stop found it gone.
        Assert.Equal("prepare", state.Events[0]);
        Assert.Equal("drop", state.Events[^1]);
        var stops = state.Events.Skip(1).SkipLast(1).ToList();
        Assert.NotEmpty(stops);
        Assert.All(stops, stop => Assert.Equal("stop: database still there", stop));
    }

    public sealed class DatabaseState : IDatabaseState
    {
        public List<string> Events { get; } = [];

        public bool Dropped { get; set; }

        public void Record(string entry) => Events.Add(entry);
    }

    public sealed class ProbeFactory : TestApiFactory<HostEntry>
    {
        private readonly DatabaseState _state;

        public ProbeFactory(DatabaseState state)
        {
            _state = state;
        }

        protected override bool RemoveHostedServices => false;

        protected override Task PrepareAsync(CancellationToken ct)
        {
            _state.Record("prepare");
            return Task.CompletedTask;
        }

        protected override Task TeardownAsync(CancellationToken ct)
        {
            _state.Record("drop");
            _state.Dropped = true;
            return Task.CompletedTask;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDatabaseState>(_state);
                services.AddSingleton<IHostedService, DatabaseProbe>();
            });
        }
    }
}
