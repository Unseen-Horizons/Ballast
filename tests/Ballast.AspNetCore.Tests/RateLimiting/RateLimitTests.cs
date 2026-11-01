// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Security.Claims;

using Ballast.AspNetCore.Tests.Support;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.AspNetCore.Tests.RateLimiting;

/// <summary>
/// The fixed-window limits: their limits are read and checked at startup, a request over a limit is answered 429 with the code and
/// <c>Retry-After</c>, and the partition key decides whose window a request counts in.
/// </summary>
public class RateLimitTests
{
    private const string AuthPath = "/auth";
    private const string PlainPath = "/plain";

    [Fact]
    public void A_limit_of_zero_from_configuration_is_refused_at_startup_and_the_message_names_the_policy()
    {
        var config = Config("RateLimits:AuthPerMinute", "0");

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRateLimits(config, options =>
            options.Policies.Add(new FixedWindowPolicy("Auth", 10))));

        Assert.Contains("'Auth'", error.Message, StringComparison.Ordinal);
        Assert.Contains("RateLimits:AuthPerMinute", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_limit_from_configuration_is_refused_at_startup()
    {
        var config = Config("RateLimits:AuthPerMinute", "-3");

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRateLimits(config, options =>
            options.Policies.Add(new FixedWindowPolicy("Auth", 10))));

        Assert.Contains("'Auth'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_default_limit_of_zero_is_refused_at_startup_and_the_message_names_the_policy()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRateLimits(new ConfigurationBuilder().Build(), options =>
            options.Policies.Add(new FixedWindowPolicy("Upload", 0))));

        Assert.Contains("'Upload'", error.Message, StringComparison.Ordinal);
        Assert.Contains("UploadPerMinute", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_window_of_zero_is_refused_at_startup_and_the_message_names_the_policy()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRateLimits(new ConfigurationBuilder().Build(), options =>
            options.Policies.Add(new FixedWindowPolicy("Auth", 5) { Window = TimeSpan.Zero })));

        Assert.Contains("'Auth'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_limit_that_is_not_a_whole_number_is_refused_and_not_replaced_by_the_default()
    {
        var config = Config("RateLimits:AuthPerMinute", "many");

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRateLimits(config, options =>
            options.Policies.Add(new FixedWindowPolicy("Auth", 10))));

        Assert.Contains("RateLimits:AuthPerMinute", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_policies_with_one_name_are_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRateLimits(new ConfigurationBuilder().Build(), options =>
        {
            options.Policies.Add(new FixedWindowPolicy("Auth", 10));
            options.Policies.Add(new FixedWindowPolicy("Auth", 20));
        }));

        Assert.Contains("'Auth'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_configured_limit_replaces_the_default()
    {
        await using var served = await Start(
            options => options.Policies.Add(new FixedWindowPolicy("Auth", 10)),
            Config("RateLimits:AuthPerMinute", "2"));

        Assert.Equal(HttpStatusCode.OK, (await served.Client.GetAsync(AuthPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await served.Client.GetAsync(AuthPath)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await served.Client.GetAsync(AuthPath)).StatusCode);
    }

    [Fact]
    public async Task A_policy_reads_the_configuration_key_it_names()
    {
        await using var served = await Start(
            options => options.Policies.Add(new FixedWindowPolicy("Auth", 10) { ConfigurationKey = "SharedPerMinute" }),
            Config("RateLimits:SharedPerMinute", "1"));

        Assert.Equal(HttpStatusCode.OK, (await served.Client.GetAsync(AuthPath)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await served.Client.GetAsync(AuthPath)).StatusCode);
    }

    [Fact]
    public async Task A_rejected_request_is_answered_429_with_the_code_retry_after_and_the_exact_problem_json()
    {
        await using var served = await Start(options => options.Policies.Add(new FixedWindowPolicy("Auth", 1)), null);
        using var first = await served.Client.GetAsync(AuthPath);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var response = await served.Client.GetAsync(AuthPath);
        var body = await response.Content.ReadAsStringAsync();
        var trace = Assert.Single(response.Headers.GetValues("X-Trace-Id"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        var retryAfter = int.Parse(Assert.Single(response.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
        Assert.Equal(
            "{\"type\":\"https://tools.ietf.org/html/rfc6585#section-4\",\"title\":\"Too Many Requests\",\"status\":429," +
            "\"detail\":\"Rate limit exceeded. Retry after the window resets.\",\"instance\":\"/auth\",\"code\":\"RATE_LIMITED\",\"traceId\":\"" + trace + "\"}",
            body);
    }

    [Fact]
    public async Task The_rejection_detail_comes_from_the_options()
    {
        await using var served = await Start(options =>
        {
            options.Policies.Add(new FixedWindowPolicy("Auth", 1));
            options.RejectionDetail = _ => "Slow down, please.";
        }, null);
        await served.Client.GetAsync(AuthPath);

        using var response = await served.Client.GetAsync(AuthPath);

        Assert.Contains("\"detail\":\"Slow down, please.\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_signed_in_user_has_a_window_of_their_own_on_one_address()
    {
        await using var served = await Start(options => options.Policies.Add(new FixedWindowPolicy("Auth", 2)), null);

        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.1", "A")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.1", "A")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(served, "10.0.0.1", "A")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.1", "B")).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_keeps_one_window_across_addresses()
    {
        await using var served = await Start(options => options.Policies.Add(new FixedWindowPolicy("Auth", 1)), null);

        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.1", "A")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(served, "10.0.0.2", "A")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_on_one_address_share_a_window()
    {
        await using var served = await Start(options => options.Policies.Add(new FixedWindowPolicy("Auth", 1)), null);

        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(served, "10.0.0.1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.9", null)).StatusCode);
    }

    [Fact]
    public async Task Two_ipv6_addresses_in_one_slash64_share_an_anonymous_window()
    {
        await using var served = await Start(options => options.Policies.Add(new FixedWindowPolicy("Auth", 1)), null);

        Assert.Equal(HttpStatusCode.OK, (await Send(served, "2001:db8:1:2::1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(served, "2001:db8:1:2::ffff", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(served, "2001:db8:1:3::1", null)).StatusCode);
    }

    [Fact]
    public async Task A_policy_that_partitions_by_address_ignores_who_is_signed_in()
    {
        await using var served = await Start(options =>
            options.Policies.Add(new FixedWindowPolicy("Auth", 1) { PartitionKey = RateLimitKeys.ForAddress }), null);

        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.1", "A")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(served, "10.0.0.1", "B")).StatusCode);
    }

    [Fact]
    public async Task The_partition_key_from_the_options_replaces_the_default_for_every_policy()
    {
        await using var served = await Start(options =>
        {
            options.Policies.Add(new FixedWindowPolicy("Auth", 1));
            options.PartitionKey = _ => "one-bucket";
        }, null);

        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(served, "10.0.0.2", null)).StatusCode);
    }

    [Fact]
    public async Task The_global_limit_applies_to_an_endpoint_with_no_policy()
    {
        await using var served = await Start(
            options => options.GlobalPolicy = new FixedWindowPolicy("Global", 100),
            Config("RateLimits:GlobalPerMinute", "1"));

        Assert.Equal(HttpStatusCode.OK, (await Send(served, "10.0.0.1", null, PlainPath)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(served, "10.0.0.1", null, PlainPath)).StatusCode);
    }

    [Fact]
    public async Task A_global_limit_with_a_zero_default_is_refused_at_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRateLimits(new ConfigurationBuilder().Build(), options =>
            options.GlobalPolicy = new FixedWindowPolicy("Global", 0)));

        Assert.Contains("'Global'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_user_key_reads_the_first_user_id_claim_the_caller_holds()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "B")], "test"));

        Assert.Equal("user:B", RateLimitKeys.ForUserOrAddress(context, ["sub", ClaimTypes.NameIdentifier]));
    }

    [Fact]
    public void A_caller_with_no_user_id_claim_is_keyed_by_its_address()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");

        Assert.Equal("ip:10.0.0.7", RateLimitKeys.ForUserOrAddress(context, ["sub"]));
        Assert.Equal("ip:10.0.0.7", RateLimitKeys.ForAddress(context));
    }

    private static IConfiguration Config(string key, string value) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();

    private static Task<Served> Start(Action<RateLimitOptions> options, IConfiguration? configuration) => Served.StartAsync(
        builder =>
        {
            if (configuration is not null)
            {
                builder.Configuration.AddConfiguration(configuration);
            }

            builder.Services.AddProblemResponses();
            builder.Services.AddRateLimits(builder.Configuration, options);
        },
        app =>
        {
            app.UseProblemResponses();
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Ip", out var ip))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                }

                if (context.Request.Headers.TryGetValue("X-Test-User", out var user) && !string.IsNullOrEmpty(user))
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test"));
                }

                await next();
            });
            app.UseRateLimiter();
            app.MapGet(AuthPath, () => "ok").RequireRateLimiting("Auth");
            app.MapGet(PlainPath, () => "ok");
        });

    private static Task<HttpResponseMessage> Send(Served served, string ip, string? user, string path = AuthPath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Ip", ip);
        if (user is not null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        return served.Client.SendAsync(request);
    }
}
