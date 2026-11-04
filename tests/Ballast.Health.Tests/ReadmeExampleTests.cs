// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.Health;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ballast.Health.Tests;

/// <summary>
/// Runs the example in src/Ballast.Health/README.md as it is written there. Three lines are the test's own, and each is marked:
/// the test host (the example runs on a port), the disposal, and the probe that stands in for <c>app.Run()</c>, which would wait
/// for shutdown. Everything else is the example, in the same order.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        string[] args = [];

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseTestServer(); // The test's own line: the example listens on a port, and the test has none.

        // The settings come from the "Health" section of configuration. The log lines go under the app's own category.
        builder.Services.AddBallastHealth(builder.Configuration, options => options.LogCategory = "Shop.Readiness");

        // A check of the app's own. Tagged ready, it joins the readiness route, the readiness log and the startup hold.
        builder.Services.AddReadinessCheck<CartStoreCheck>("cart-store");

        // The startup hold goes last. It does nothing unless Health:StartupHoldMaxSeconds is above zero.
        builder.Services.AddStartupHold();

        await using var app = builder.Build(); // The test's own: "await using", so the test host is disposed.
        app.MapBallastHealth();

        // The example calls app.Run() here. That waits for shutdown, so the test starts the app and probes it instead.
        await app.StartAsync(); // The test's own line.
        var response = await app.GetTestClient().GetAsync("/health/ready"); // The test's own line.

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"Healthy\",\"checks\":{\"cart-store\":\"Healthy\"}}", await response.Content.ReadAsStringAsync());
    }

    /// <summary>The example's check of the app's own.</summary>
    private sealed class CartStoreCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy("The cart store answers."));
    }
}
