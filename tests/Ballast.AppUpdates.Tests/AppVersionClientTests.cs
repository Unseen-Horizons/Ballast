// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Microsoft.Extensions.Logging;

namespace Ballast.AppUpdates.Tests;

/// <summary>
/// The app's ask of the version endpoint. A failure is advisory: it is logged and not answered. A 404 is an answer, "no minimum".
/// </summary>
public class AppVersionClientTests
{
    private const string Ok = """
        {"platform":"Android","latestBuild":12,"minimumBuild":8,"latestVersion":"0.2.0",
         "storeUrl":"https://store.example.test/app","channel":"play","messages":{"en":"Fixes sign-in."}}
        """;

    private static HttpClient Http(FakeHttpHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.example.test/") };

    [Fact]
    public async Task A_usable_answer_is_read_with_the_wire_names()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.OK, Ok));
        var client = new AppVersionClient(Http(handler), new CapturingLogger<AppVersionClient>());

        var answer = await client.GetAsync("android", "play");

        Assert.True(answer.Answered);
        Assert.NotNull(answer.Info);
        Assert.Equal("Android", answer.Info.Platform);
        Assert.Equal(12, answer.Info.LatestBuild);
        Assert.Equal(8, answer.Info.MinimumBuild);
        Assert.Equal("0.2.0", answer.Info.LatestVersion);
        Assert.Equal("https://store.example.test/app", answer.Info.StoreUrl);
        Assert.Equal("play", answer.Info.Channel);
        Assert.Equal("Fixes sign-in.", answer.Info.Messages["en"]);
    }

    [Fact]
    public async Task The_platform_and_channel_are_sent_as_the_query()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.OK, Ok));

        await new AppVersionClient(Http(handler)).GetAsync("android", "direct");

        Assert.Equal("https://api.example.test/app/version?platform=android&channel=direct", handler.Urls.Single().AbsoluteUri);
    }

    [Fact]
    public async Task A_missing_channel_is_left_out_of_the_query()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.OK, Ok));

        await new AppVersionClient(Http(handler)).GetAsync("ios", channel: null);

        Assert.Equal("https://api.example.test/app/version?platform=ios", handler.Urls.Single().AbsoluteUri);
    }

    [Fact]
    public async Task The_platform_is_escaped_in_the_query()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.OK, Ok));

        await new AppVersionClient(Http(handler)).GetAsync("a b&c");

        Assert.Contains("platform=a%20b%26c", handler.Urls.Single().AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_route_is_an_option()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.OK, Ok));

        await new AppVersionClient(Http(handler), options: new AppUpdateOptions { Route = "api/version" }).GetAsync("android");

        Assert.StartsWith("https://api.example.test/api/version?platform=android", handler.Urls.Single().AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_404_is_an_answer_of_no_minimum()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.NotFound, """{"code":"APP_VERSION_NOT_CONFIGURED","title":"No app version is configured for this platform."}"""));

        var answer = await new AppVersionClient(Http(handler), new CapturingLogger<AppVersionClient>()).GetAsync("android");

        Assert.True(answer.Answered);
        Assert.Null(answer.Info);
    }

    [Fact]
    public async Task A_404_is_logged_once_for_the_client()
    {
        var logger = new CapturingLogger<AppVersionClient>();
        var handler = new FakeHttpHandler(
            FakeHttpHandler.Json(HttpStatusCode.NotFound, """{"code":"APP_VERSION_NOT_CONFIGURED"}"""),
            FakeHttpHandler.Json(HttpStatusCode.NotFound, """{"code":"APP_VERSION_NOT_CONFIGURED"}"""));
        var client = new AppVersionClient(Http(handler), logger);

        await client.GetAsync("android");
        await client.GetAsync("android");

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task A_server_error_is_not_answered_and_is_logged_with_its_code()
    {
        var logger = new CapturingLogger<AppVersionClient>();
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.InternalServerError, """{"code":"APP_VERSION_MISCONFIGURED","detail":"x","traceId":"t-1"}"""));

        var answer = await new AppVersionClient(Http(handler), logger).GetAsync("android");

        Assert.False(answer.Answered);
        Assert.Null(answer.Info);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("APP_VERSION_MISCONFIGURED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_answer_with_no_usable_build_number_is_not_answered()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.OK, """{"platform":"Android","latestBuild":0,"minimumBuild":0}"""));

        var answer = await new AppVersionClient(Http(handler), new CapturingLogger<AppVersionClient>()).GetAsync("android");

        Assert.False(answer.Answered);
    }

    [Fact]
    public async Task A_minimum_only_answer_is_usable()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.OK, """{"platform":"Android","latestBuild":0,"minimumBuild":9,"channel":"play"}"""));

        var answer = await new AppVersionClient(Http(handler)).GetAsync("android", "play");

        Assert.True(answer.Answered);
        Assert.Equal(9, answer.Info!.MinimumBuild);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_not_answered()
    {
        var handler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") });

        var answer = await new AppVersionClient(Http(handler), new CapturingLogger<AppVersionClient>()).GetAsync("android");

        Assert.False(answer.Answered);
    }

    [Fact]
    public async Task A_connection_failure_is_not_answered_and_does_not_throw()
    {
        var handler = new FakeHttpHandler { ThrowOnSend = new HttpRequestException("No network.") };

        var answer = await new AppVersionClient(Http(handler), new CapturingLogger<AppVersionClient>()).GetAsync("android");

        Assert.False(answer.Answered);
    }

    [Fact]
    public async Task A_timeout_is_not_answered_and_does_not_throw()
    {
        var handler = new FakeHttpHandler { ThrowOnSend = new TaskCanceledException("Timed out.") };

        var answer = await new AppVersionClient(Http(handler)).GetAsync("android");

        Assert.False(answer.Answered);
    }

    [Fact]
    public async Task A_cancelled_call_is_not_swallowed()
    {
        var handler = new FakeHttpHandler(FakeHttpHandler.Json(HttpStatusCode.OK, Ok));
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AppVersionClient(Http(handler)).GetAsync("android", ct: cancel.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_platform_is_refused(string platform) =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new AppVersionClient(Http(new FakeHttpHandler())).GetAsync(platform));

    [Fact]
    public void A_missing_http_client_is_refused() =>
        Assert.Throws<ArgumentNullException>(() => new AppVersionClient(null!));
}
