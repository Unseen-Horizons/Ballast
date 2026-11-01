// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Health.Tests;

/// <summary>
/// The three health routes on a real pipeline: the build route's body, the liveness route answering while readiness is
/// Unhealthy, that all three answer anonymous requests under a require-sign-in fallback, that all three are exempt from a rate
/// limit that is exhausted, and that the warm-up starts with the application and opens its gate.
/// </summary>
public class HealthEndpointTests
{
    /// <summary>A check whose answer the test sets, and which counts its own calls.</summary>
    private sealed class SettableCheck : IHealthCheck
    {
        public HealthStatus Answer { get; set; } = HealthStatus.Healthy;

        public int Calls;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HealthCheckResult(Answer, "Scripted."));
        }
    }

    /// <summary>An authentication scheme that never finds a user, so a require-sign-in fallback answers 401 rather than 500.</summary>
    private sealed class NoUserHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public NoUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    /// <summary>A warm-up step that records that it ran.</summary>
    private sealed class RecordingStep : IWarmupStep
    {
        public string Name => "recording";

        public int Runs;

        public Task RunAsync(IServiceProvider services, CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_build_route_answers_with_the_build_and_the_clock()
    {
        var time = new ManualTime();

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddSingleton<TimeProvider>(time);
                builder.Services.AddBallastHealth(builder.Configuration, options =>
                {
                    options.BuildAssembly = typeof(BuildInfoTests).Assembly;
                    options.BuiltAtMetadataKey = "Ballast.Health.TestBuiltAt";
                });
            },
            app => app.MapBuildInfo(),
            async (_, client) =>
            {
                var root = await client.GetFromJsonAsync<JsonElement>("/health");

                Assert.Equal("healthy", root.GetProperty("status").GetString());
                Assert.Equal("2026-10-21T10:00:00Z", root.GetProperty("timestamp").GetString());
                Assert.Equal("2026-10-21T10:00:00Z", root.GetProperty("builtAtUtc").GetString());
                Assert.True(root.TryGetProperty("startedUtc", out var started) && started.ValueKind == JsonValueKind.String);
            });
    }

    [Fact]
    public async Task The_liveness_route_answers_200_while_readiness_is_unhealthy()
    {
        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddBallastHealth(builder.Configuration);
                builder.Services.AddHealthChecks().AddCheck("down", () => HealthCheckResult.Unhealthy(), tags: [ReadinessEndpoint.ReadyTag]);
            },
            app => app.MapBallastHealth(),
            async (_, client) =>
            {
                var live = await client.GetAsync("/health/live");

                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
                Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            });
    }

    [Fact]
    public async Task All_three_routes_answer_anonymous_requests_under_a_require_sign_in_fallback()
    {
        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddAuthentication("NoUser").AddScheme<AuthenticationSchemeOptions, NoUserHandler>("NoUser", _ => { });
                builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                builder.Services.AddBallastHealth(builder.Configuration);
            },
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapBallastHealth();
                app.MapGet("/private", () => Results.Ok("secret")).RequireAuthorization();
            },
            async (_, client) =>
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

                // The control: the fallback does refuse an endpoint that does not allow anonymous callers.
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/private")).StatusCode);
            });
    }

    [Fact]
    public async Task All_three_routes_are_exempt_from_a_rate_limit_that_is_exhausted()
    {
        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddRateLimiter(limiter =>
                {
                    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                        _ => RateLimitPartition.GetFixedWindowLimiter("all", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 1,
                            Window = TimeSpan.FromHours(1),
                            QueueLimit = 0,
                        }));
                });
                builder.Services.AddBallastHealth(builder.Configuration);
            },
            app =>
            {
                app.UseRateLimiter();
                app.MapBallastHealth();
                app.MapGet("/limited", () => Results.Ok("ok"));
            },
            async (_, client) =>
            {
                // The one permit is spent on the control, so the limiter now refuses everything it covers.
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/limited")).StatusCode);
                Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/limited")).StatusCode);

                for (var i = 0; i < 3; i++)
                {
                    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
                }
            });
    }

    [Fact]
    public async Task The_warm_up_starts_with_the_application_and_opens_its_gate()
    {
        var step = new RecordingStep();

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddBallastHealth(builder.Configuration);
                builder.Services.AddSingleton<IWarmupStep>(step);
                builder.Services.AddWarmupGate(TimeSpan.FromMinutes(5));
            },
            app => app.MapBallastHealth(),
            async (_, client) =>
            {
                // The warm-up runs on a background thread, so the first probes may still see it unfinished. Probe until it is met.
                HttpResponseMessage? response = null;
                for (var i = 0; i < 250; i++)
                {
                    response = await client.GetAsync("/health/ready");
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        break;
                    }
                    await Task.Delay(20);
                }

                Assert.NotNull(response);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(1, step.Runs);
                Assert.Equal("{\"status\":\"Healthy\",\"checks\":{\"warmup\":\"Healthy\"}}", await response.Content.ReadAsStringAsync());
            });
    }

    [Fact]
    public async Task A_check_added_from_outside_is_consulted_on_each_probe_and_keeps_its_state()
    {
        SettableCheck? check = null;

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddBallastHealth(builder.Configuration);
                builder.Services.AddReadinessCheck<SettableCheck>("external");
            },
            app =>
            {
                check = app.Services.GetRequiredService<SettableCheck>();
                app.MapBallastHealth();
            },
            async (_, client) =>
            {
                var scripted = check ?? throw new InvalidOperationException("The check was not resolved.");
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
                scripted.Answer = HealthStatus.Unhealthy;
                Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);

                // The same instance answered both probes.
                Assert.Equal(2, scripted.Calls);
            });
    }

    [Fact]
    public async Task The_readiness_body_names_a_gate_met_by_its_fact()
    {
        var flag = new FactFlag();

        await TestHosts.RunAsync(
            builder =>
            {
                builder.Services.AddBallastHealth(builder.Configuration);
                builder.Services.AddSingleton(flag);
                builder.Services.AddStartupGate("fact", sp => sp.GetRequiredService<FactFlag>().On, TimeSpan.FromMinutes(5));
            },
            app => app.MapReadiness(),
            async (_, client) =>
            {
                var first = await client.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
                Assert.Equal("{\"status\":\"Unhealthy\",\"checks\":{\"fact\":\"Unhealthy\"}}", await first.Content.ReadAsStringAsync());

                flag.On = true;
                var second = await client.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.OK, second.StatusCode);
                Assert.Equal("{\"status\":\"Healthy\",\"checks\":{\"fact\":\"Healthy\"}}", await second.Content.ReadAsStringAsync());
            });
    }

    /// <summary>A fact the test turns on.</summary>
    private sealed class FactFlag
    {
        public bool On { get; set; }
    }
}
