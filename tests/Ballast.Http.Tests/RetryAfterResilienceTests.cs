// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Http.Tests;

/// <summary>
/// The Retry-After retry on a named client: which answers are tried again, how long each wait is, and which failures are never tried
/// again (a refusal by the safety rules, and the client's own timeout). The waits are kept short, so the tests run in well under a second.
/// </summary>
public class RetryAfterResilienceTests
{
    [Fact]
    public async Task A_busy_answer_is_tried_again_after_its_retry_after_capped_at_the_maximum_and_the_success_is_returned()
    {
        var web = FirstThen(
            () => Answer.WithStatus(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromHours(1))),
            () => Answer.Text(HttpStatusCode.OK, "fine"));
        var client = Client(web, o => o.MaxRetryAfter = TimeSpan.FromMilliseconds(30));

        var clock = Stopwatch.StartNew();
        using var response = await client.GetAsync("https://93.184.216.34/x");
        clock.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, web.Requests.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "The hour-long Retry-After was not capped.");
    }

    [Fact]
    public async Task Without_a_retry_after_the_default_delays_are_used_and_the_last_busy_answer_is_returned()
    {
        var web = new ScriptedWeb(_ => Answer.WithStatus(HttpStatusCode.ServiceUnavailable));
        var client = Client(web, o => o.DefaultDelays = [TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(400)]);

        var clock = Stopwatch.StartNew();
        using var response = await client.GetAsync("https://93.184.216.34/x");
        clock.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, web.Requests.Count);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(400), $"The two default waits were not both used: {clock.Elapsed}.");
    }

    [Fact]
    public async Task A_retry_past_the_last_default_delay_repeats_the_last_one()
    {
        var web = new ScriptedWeb(_ => Answer.WithStatus(HttpStatusCode.BadGateway));
        var client = Client(web, o =>
        {
            o.MaxAttempts = 4;
            o.DefaultDelays = [TimeSpan.FromMilliseconds(5)];
        });

        using var response = await client.GetAsync("https://93.184.216.34/x");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(4, web.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task The_four_busy_statuses_are_tried_again_by_default(HttpStatusCode status)
    {
        var web = FirstThen(() => Answer.WithStatus(status), () => Answer.Text(HttpStatusCode.OK, "fine"));
        var client = Client(web, o => o.DefaultDelays = [TimeSpan.FromMilliseconds(1)]);

        using var response = await client.GetAsync("https://93.184.216.34/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, web.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_status_that_is_not_a_busy_one_is_the_answer_at_once(HttpStatusCode status)
    {
        var web = new ScriptedWeb(_ => Answer.WithStatus(status));
        var client = Client(web, o => o.DefaultDelays = [TimeSpan.FromMilliseconds(1)]);

        using var response = await client.GetAsync("https://93.184.216.34/x");

        Assert.Equal(status, response.StatusCode);
        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task The_retry_statuses_are_an_option()
    {
        var web = FirstThen(() => Answer.WithStatus((HttpStatusCode)418), () => Answer.Text(HttpStatusCode.OK, "fine"));
        var client = Client(web, o =>
        {
            o.RetryStatuses = [(HttpStatusCode)418];
            o.DefaultDelays = [TimeSpan.FromMilliseconds(1)];
        });

        using var response = await client.GetAsync("https://93.184.216.34/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, web.Requests.Count);
    }

    [Fact]
    public async Task A_connection_failure_is_tried_again()
    {
        var web = FirstThen(
            () => throw new HttpRequestException("connection reset"),
            () => Answer.Text(HttpStatusCode.OK, "fine"));
        var client = Client(web, o => o.DefaultDelays = [TimeSpan.FromMilliseconds(1)]);

        using var response = await client.GetAsync("https://93.184.216.34/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, web.Requests.Count);
    }

    [Fact]
    public async Task A_connection_failure_is_not_tried_again_when_that_is_turned_off()
    {
        var web = new ScriptedWeb(_ => throw new HttpRequestException("connection reset"));
        var client = Client(web, o => o.RetryOnConnectionFailure = false);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://93.184.216.34/x"));

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_refusal_by_the_safety_rules_is_never_tried_again()
    {
        var web = new ScriptedWeb(_ => throw new HttpRequestException(
            "connect refused", new HttpSafetyException(HttpSafetyReason.NonPublicAddress, "resolves to a non-public address.")));
        var client = Client(web, o => o.DefaultDelays = [TimeSpan.FromMilliseconds(1)]);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://93.184.216.34/x"));

        Assert.IsType<HttpSafetyException>(ex.InnerException);
        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_timeout_is_never_tried_again()
    {
        var web = new ScriptedWeb(_ => throw new TaskCanceledException("timed out"));
        var client = Client(web, o => o.DefaultDelays = [TimeSpan.FromMilliseconds(1)]);

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetAsync("https://93.184.216.34/x"));

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task One_attempt_means_no_retry()
    {
        var web = new ScriptedWeb(_ => Answer.WithStatus(HttpStatusCode.ServiceUnavailable));
        var client = Client(web, o => o.MaxAttempts = 1);

        using var response = await client.GetAsync("https://93.184.216.34/x");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(web.Requests);
    }

    [Fact]
    public void The_options_are_checked_at_registration()
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("bad");

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRetryAfterResilience(o => o.MaxAttempts = 0));
        Assert.Throws<ArgumentException>(() => builder.AddRetryAfterResilience(o => o.DefaultDelays = []));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRetryAfterResilience(o => o.MaxRetryAfter = TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentException>(() => builder.AddRetryAfterResilience(o => o.DefaultDelays = [TimeSpan.FromSeconds(-1)]));
    }

    /// <summary>Answers the first request with <paramref name="first"/>, and every request after it with <paramref name="then"/>.</summary>
    private static ScriptedWeb FirstThen(Func<HttpResponseMessage> first, Func<HttpResponseMessage> then)
    {
        var calls = 0;
        return new ScriptedWeb(_ => ++calls == 1 ? first() : then());
    }

    private static HttpClient Client(ScriptedWeb web, Action<RetryAfterOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("retry")
            .ConfigurePrimaryHttpMessageHandler(() => web)
            .AddRetryAfterResilience(configure);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientFactory>().CreateClient("retry");
    }
}
