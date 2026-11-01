// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ballast.Health.Tests;

/// <summary>
/// <c>/health/ready</c> on a real pipeline (an in-memory test server, with the shared readiness wiring and fake checks):
/// the status codes, what the body names, that nothing leaks, and that a probe never causes a database ask of its own.
/// </summary>
public class ReadinessEndpointTests
{
    private static Task WithReadinessAsync(Action<IServiceCollection> register, Func<HttpClient, Task> probe) =>
        TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddBallastHealth(builder.Configuration, options => options.LogCategory = "Test.Readiness");
                register(builder.Services);
            },
            app => app.MapReadiness(),
            (_, client) => probe(client));

    private static void SingleCheck(IServiceCollection services, Func<HealthCheckResult> check) =>
        services.AddHealthChecks().AddCheck("ok", check, tags: [ReadinessEndpoint.ReadyTag]);

    [Fact]
    public async Task A_healthy_copy_answers_200()
    {
        await WithReadinessAsync(s => SingleCheck(s, () => HealthCheckResult.Healthy()), async client =>
        {
            var response = await client.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
    }

    [Fact]
    public async Task A_degraded_copy_is_still_ready_and_answers_200()
    {
        await WithReadinessAsync(s => SingleCheck(s, () => HealthCheckResult.Degraded()), async client =>
        {
            var response = await client.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
    }

    [Fact]
    public async Task An_unhealthy_copy_answers_503()
    {
        await WithReadinessAsync(s => SingleCheck(s, () => HealthCheckResult.Unhealthy()), async client =>
        {
            var response = await client.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        });
    }

    [Fact]
    public async Task The_body_names_each_check_and_its_status_and_nothing_else()
    {
        await WithReadinessAsync(s => SingleCheck(s, () => HealthCheckResult.Degraded("a description that must not be shown")), async client =>
        {
            var body = await (await client.GetAsync("/health/ready")).Content.ReadAsStringAsync();

            Assert.Equal("{\"status\":\"Degraded\",\"checks\":{\"ok\":\"Degraded\"}}", body);
        });
    }

    [Fact]
    public async Task A_check_that_throws_with_a_connection_string_leaks_nothing()
    {
        await WithReadinessAsync(s => SingleCheck(s, () => throw new InvalidOperationException("Server=db;Password=hunter2")), async client =>
        {
            var response = await client.GetAsync("/health/ready");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task The_answer_is_never_cached()
    {
        await WithReadinessAsync(s => SingleCheck(s, () => HealthCheckResult.Healthy()), async client =>
        {
            var response = await client.GetAsync("/health/ready");

            Assert.True(response.Headers.CacheControl?.NoStore ?? false);
        });
    }

    [Fact]
    public async Task Fifty_probes_make_at_most_one_database_ask()
    {
        var reachability = new FakeReachability();

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddBallastHealth(builder.Configuration, options => options.LogCategory = "Test.Readiness");
                builder.Services.AddSingleton<IDatabaseReachability>(reachability);
                builder.Services.AddSingleton<DatabaseHealthCheck>();
                builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [ReadinessEndpoint.ReadyTag]);
            },
            app => app.MapReadiness(),
            async (_, client) =>
            {
                for (var i = 0; i < 50; i++)
                {
                    await client.GetAsync("/health/ready");
                }
            });

        Assert.Equal(1, reachability.Asks);
    }
}
