// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Jobs;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ballast.Jobs.Tests;

/// <summary>
/// Runs the example in src/Ballast.Jobs/README.md as it is written there. Three lines are the test's own, and each is marked:
/// the test host (the example listens on a port, and the test has none), the start and the stop that stand in for <c>app.Run()</c>,
/// which waits for shutdown, and the checks the test makes. Everything else is the example, in the same order.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        string[] args = [];

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseTestServer(); // The test's own line: the example listens on a port, and the test has none.

        var connectionString = builder.Configuration.GetConnectionString("Shop")
            ?? "Server=localhost;Database=Shop;Trusted_Connection=True;TrustServerCertificate=True";

        // The lock goes first: it starts before every other hosted service and stops after them. It is off until
        // JobLeadership:Enabled is true, and then only the copy that holds the lock runs the workers below.
        builder.Services.AddJobLeadership(builder.Configuration, "Shop:jobs", connectionString);
        builder.Services.ConfigureGracefulShutdown(builder.Configuration);

        // A worker that writes. It runs only while this copy holds the lock.
        builder.Services.AddLeaderHostedService<OrderExpiryWorker>();

        await using var app = builder.Build(); // The test's own: "await using", so the test host is disposed.

        // The example calls app.Run() here. That waits for shutdown, so the test starts the app, checks it, and stops it.
        await app.StartAsync(); // The test's own line.
        Assert.True(app.Services.GetRequiredService<IJobLeadership>().IsHolder); // The test's own line: the lock is off, so this copy holds it.
        await app.StopAsync(); // The test's own line.
    }

    /// <summary>The example's worker: it expires orders on a timer, and ends when the host or the lock's term ends it.</summary>
    private sealed class OrderExpiryWorker : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // ... expire the orders that are due ...
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
