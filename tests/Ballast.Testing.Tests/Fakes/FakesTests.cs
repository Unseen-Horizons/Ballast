// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;

using Ballast.Testing.Fakes;

using Microsoft.Extensions.Logging;

namespace Ballast.Testing.Tests.Fakes;

public class FakesTests
{
    [Fact]
    public void Captured_logs_keep_each_entry_with_its_category_level_message_and_open_scopes()
    {
        var logs = new CapturedLogs();
        var logger = logs.CreateLogger("Shop.Orders");

        using (logger.BeginScope("request-1"))
        {
            using (logger.BeginScope("order-9"))
            {
                logger.LogWarning("Order {Id} is late", 9);
            }
        }

        var entry = Assert.Single(logs.Records);
        Assert.Equal("Shop.Orders", entry.Category);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Order 9 is late", entry.Message);
        Assert.Equal(["request-1", "order-9"], entry.Scopes);
    }

    [Fact]
    public void A_scope_is_removed_once_it_ends()
    {
        var logs = new CapturedLogs();
        var logger = logs.CreateLogger("Any");

        using (logger.BeginScope("temporary"))
        {
            logger.LogInformation("inside");
        }

        logger.LogInformation("outside");

        Assert.Equal("temporary", Assert.Single(logs.Records[0].Scopes));
        Assert.Empty(logs.Records[1].Scopes);
    }

    [Fact]
    public void The_records_snapshot_does_not_grow_with_later_entries()
    {
        var logs = new CapturedLogs();
        var logger = logs.CreateLogger("Any");
        logger.LogInformation("first");
        var snapshot = logs.Records;

        logger.LogInformation("second");

        Assert.Single(snapshot);
        Assert.Equal(2, logs.Records.Count);
    }

    [Fact]
    public async Task A_scripted_handler_records_each_request_with_its_body_and_answers_from_the_script()
    {
        var handler = new ScriptedHttpHandler(_ => HttpAnswers.Text(HttpStatusCode.Created, "made"));
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/items")
        {
            Content = new StringContent("{\"a\":1}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "t0ken");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("made", await response.Content.ReadAsStringAsync());
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri("https://example.test/items"), sent.Url);
        Assert.Equal("Bearer t0ken", sent.Authorization);
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal("{\"a\":1}", sent.Body);
        Assert.Equal([new Uri("https://example.test/items")], handler.RequestedUrls);
    }

    [Fact]
    public async Task A_scripted_handler_that_throws_still_records_the_request()
    {
        var handler = new ScriptedHttpHandler(_ => HttpAnswers.Text(HttpStatusCode.OK, "unused"))
        {
            ThrowOnSend = new HttpRequestException("refused by the test"),
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.test/"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_scripted_handler_cancels_the_callers_token_during_the_send()
    {
        using var source = new CancellationTokenSource();
        var handler = new ScriptedHttpHandler(_ => HttpAnswers.Text(HttpStatusCode.OK, "unused"))
        {
            CancelOnSend = source,
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test/", source.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_queue_of_responses_answers_in_order_and_fails_loudly_when_it_runs_out()
    {
        var handler = ScriptedHttpHandler.FromResponses(
            HttpAnswers.Text(HttpStatusCode.OK, "one"),
            HttpAnswers.Text(HttpStatusCode.Accepted, "two"));
        using var client = new HttpClient(handler);

        Assert.Equal("one", await client.GetStringAsync("https://example.test/a"));
        Assert.Equal("two", await client.GetStringAsync("https://example.test/b"));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => client.GetAsync("https://example.test/c"));
    }

    [Fact]
    public void A_null_script_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new ScriptedHttpHandler(null!));
    }

    [Fact]
    public async Task The_stub_factory_hands_out_clients_over_its_handler_for_any_name()
    {
        var handler = ScriptedHttpHandler.FromResponses(HttpAnswers.Text(HttpStatusCode.OK, "ok"));
        var factory = new StubHttpClientFactory(handler);

        using var client = factory.CreateClient("anything");

        Assert.Equal("ok", await client.GetStringAsync("https://example.test/"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_answer_builders_give_their_status_headers_and_bodies()
    {
        using var redirect = HttpAnswers.RedirectTo("/next");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal(new Uri("/next", UriKind.Relative), redirect.Headers.Location);

        using var limited = HttpAnswers.WithStatus(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)));
        Assert.Equal(TimeSpan.FromSeconds(7), limited.Headers.RetryAfter!.Delta);

        using var bytes = HttpAnswers.Ok([1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await bytes.Content.ReadAsByteArrayAsync());

        using var streamed = HttpAnswers.Streamed("streamed text");
        Assert.Equal("streamed text", await streamed.Content.ReadAsStringAsync());
    }

    [Fact]
    public void An_environment_variable_is_set_for_the_scope_and_put_back_after_it()
    {
        const string name = "BALLAST_TESTING_SCOPE_CHECK";
        Environment.SetEnvironmentVariable(name, "before");
        try
        {
            using (EnvironmentVariableScope.Set(name, "during"))
            {
                Assert.Equal("during", Environment.GetEnvironmentVariable(name));
            }

            Assert.Equal("before", Environment.GetEnvironmentVariable(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void A_variable_that_was_unset_is_unset_again_and_a_second_dispose_changes_nothing()
    {
        const string name = "BALLAST_TESTING_UNSET_CHECK";
        Environment.SetEnvironmentVariable(name, null);

        var scope = EnvironmentVariableScope.Set(name, "set");
        Assert.Equal("set", Environment.GetEnvironmentVariable(name));

        scope.Dispose();
        Assert.Null(Environment.GetEnvironmentVariable(name));

        Environment.SetEnvironmentVariable(name, "later");
        scope.Dispose();
        Assert.Equal("later", Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, null);
    }

    [Fact]
    public void A_blank_variable_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => EnvironmentVariableScope.Set(" ", "x"));
    }
}
