// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Ballast.Http.Tests;

/// <summary>
/// The polite fetcher, ported from GetThere's tests, against a scripted web: robots.txt first, the refusals, redirects checked hop by hop, the body cap, the spacing
/// per host, and the answers that are results rather than exceptions. Hosts are public IP literals, so no name is resolved. Nothing
/// leaves the test process; the clock and the wait are the test's own, so nothing sleeps.
/// </summary>
public class PoliteFetcherTests
{
    private const string Page = "https://93.184.216.34/timetable";
    private const string Robots = "https://93.184.216.34/robots.txt";
    private const long Cap = 1024;

    [Fact]
    public async Task An_allowed_page_is_read_with_its_status_body_and_final_url()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.OK, "User-agent: *\nDisallow: /private\n")
            : Reply(HttpStatusCode.OK, "hello"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.False(result.Refused);
        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal("hello", result.Text);
        Assert.Equal(Page, result.FinalUrl.AbsoluteUri);
        Assert.Equal(new[] { Robots, Page }, web.Requested.Select(u => u.AbsoluteUri).ToArray());
    }

    [Fact]
    public async Task A_disallowed_path_is_refused_and_its_page_is_never_requested()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.OK, "User-agent: *\nDisallow: /private\n")
            : Reply(HttpStatusCode.OK, "secret"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri("https://93.184.216.34/private/x"), Cap);

        Assert.Equal(PoliteRefusal.RobotsDisallowed, result.Refusal);
        Assert.DoesNotContain(web.Requested, u => u.AbsolutePath == "/private/x");
    }

    [Fact]
    public async Task The_robots_answer_is_kept_for_a_day_and_read_again_after_that()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.OK, "User-agent: *\n")
            : Reply(HttpStatusCode.OK, "ok"));
        var clock = new Clock();
        var fetcher = NewFetcher(web, clock, []);

        await fetcher.GetAsync(new Uri(Page), Cap);
        await fetcher.GetAsync(new Uri(Page), Cap);
        Assert.Equal(1, web.Requested.Count(u => u.AbsolutePath == "/robots.txt"));

        clock.Advance(TimeSpan.FromHours(25));
        await fetcher.GetAsync(new Uri(Page), Cap);
        Assert.Equal(2, web.Requested.Count(u => u.AbsolutePath == "/robots.txt"));
    }

    [Fact]
    public async Task A_robots_server_error_disallows_everything_and_the_page_is_not_requested()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.ServiceUnavailable, "down")
            : Reply(HttpStatusCode.OK, "ok"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RobotsDisallowed, result.Refusal);
        Assert.Contains("503", result.Detail);
        Assert.DoesNotContain(web.Requested, u => u.AbsolutePath == "/timetable");
    }

    [Fact]
    public async Task A_robots_client_error_allows_everything()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : Reply(HttpStatusCode.OK, "ok"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.False(result.Refused);
        Assert.Equal("ok", result.Text);
    }

    [Fact]
    public async Task A_robots_that_cannot_be_reached_disallows_everything()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? throw new HttpRequestException("connection refused")
            : Reply(HttpStatusCode.OK, "ok"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RobotsDisallowed, result.Refusal);
        Assert.Contains("could not be fetched", result.Detail);
    }

    [Fact]
    public async Task A_url_that_is_not_https_or_embeds_credentials_is_refused_before_any_request()
    {
        var web = new ScriptedWeb(_ => Reply(HttpStatusCode.OK, "x"));
        var fetcher = NewFetcher(web, new Clock(), []);

        var plain = await fetcher.GetAsync(new Uri("http://93.184.216.34/timetable"), Cap);
        var embedded = await fetcher.GetAsync(new Uri("https://user:pw@93.184.216.34/timetable"), Cap);

        Assert.Equal(PoliteRefusal.InvalidUrl, plain.Refusal);
        Assert.Equal(PoliteRefusal.InvalidUrl, embedded.Refusal);
        Assert.Empty(web.Requested);
    }

    [Theory]
    [InlineData("https://10.0.0.5/timetable")]
    [InlineData("https://127.0.0.1/timetable")]
    public async Task A_private_address_is_refused_before_any_request(string url)
    {
        var web = new ScriptedWeb(_ => Reply(HttpStatusCode.OK, "x"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(url), Cap);

        Assert.Equal(PoliteRefusal.NotPublic, result.Refusal);
        Assert.Empty(web.Requested);
    }

    [Fact]
    public async Task A_redirect_to_a_private_address_is_refused_and_that_address_is_not_requested()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : RedirectTo("https://10.0.0.5/secret"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RedirectRefused, result.Refusal);
        Assert.DoesNotContain(web.Requested, u => u.Host == "10.0.0.5");
    }

    [Fact]
    public async Task A_redirect_to_plain_http_is_refused()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : RedirectTo("http://93.184.216.34/other"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RedirectRefused, result.Refusal);
    }

    [Fact]
    public async Task A_redirect_with_no_location_is_refused_and_its_target_is_never_requested()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : new HttpResponseMessage(HttpStatusCode.Found));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RedirectRefused, result.Refusal);
        Assert.Equal(2, web.Requested.Count);
    }

    [Fact]
    public async Task A_redirect_whose_location_is_not_a_url_is_refused_as_a_redirect()
    {
        var web = new ScriptedWeb(uri =>
        {
            if (uri.AbsolutePath == "/robots.txt")
            {
                return Reply(HttpStatusCode.NotFound, string.Empty);
            }

            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.TryAddWithoutValidation("Location", "http://[bad");
            return response;
        });

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RedirectRefused, result.Refusal);
        Assert.Equal(2, web.Requested.Count);
    }

    [Fact]
    public async Task A_robots_redirect_with_no_location_disallows_everything_and_the_page_is_not_requested()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? new HttpResponseMessage(HttpStatusCode.Found)
            : Reply(HttpStatusCode.OK, "page"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RobotsDisallowed, result.Refusal);
        Assert.Equal("/robots.txt", Assert.Single(web.Requested).AbsolutePath);
    }

    [Fact]
    public void A_negative_cap_is_an_argument_error_before_anything_is_requested()
    {
        var web = new ScriptedWeb(_ => Reply(HttpStatusCode.OK, "page"));
        var fetcher = NewFetcher(web, new Clock(), []);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = fetcher.GetAsync(new Uri(Page), -1); });
        Assert.Empty(web.Requested);
    }

    [Fact]
    public async Task A_redirect_is_followed_and_the_target_hosts_robots_txt_is_read_first()
    {
        var web = new ScriptedWeb(uri => uri switch
        {
            { Host: "93.184.216.34", AbsolutePath: "/robots.txt" } => Reply(HttpStatusCode.NotFound, string.Empty),
            { Host: "93.184.216.34", AbsolutePath: "/timetable" } => RedirectTo("https://198.51.100.7/landing"),
            { Host: "198.51.100.7", AbsolutePath: "/robots.txt" } => Reply(HttpStatusCode.OK, "User-agent: *\nDisallow: /admin\n"),
            _ => Reply(HttpStatusCode.OK, "landed"),
        });

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.False(result.Refused);
        Assert.Equal("landed", result.Text);
        Assert.Equal("https://198.51.100.7/landing", result.FinalUrl.AbsoluteUri);
        Assert.Contains(web.Requested, u => u.AbsoluteUri == "https://198.51.100.7/robots.txt");
    }

    [Fact]
    public async Task A_redirect_target_its_robots_txt_disallows_is_refused_and_not_requested()
    {
        var web = new ScriptedWeb(uri => uri switch
        {
            { Host: "93.184.216.34", AbsolutePath: "/robots.txt" } => Reply(HttpStatusCode.NotFound, string.Empty),
            { Host: "93.184.216.34", AbsolutePath: "/timetable" } => RedirectTo("https://198.51.100.7/landing"),
            { Host: "198.51.100.7", AbsolutePath: "/robots.txt" } => Reply(HttpStatusCode.OK, "User-agent: *\nDisallow: /landing\n"),
            _ => Reply(HttpStatusCode.OK, "landed"),
        });

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RobotsDisallowed, result.Refusal);
        Assert.DoesNotContain(web.Requested, u => u.AbsoluteUri == "https://198.51.100.7/landing");
    }

    [Fact]
    public async Task Redirects_past_five_hops_are_refused()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : RedirectTo("https://93.184.216.34/r" + (int.Parse(uri.AbsolutePath[2..], CultureInfo.InvariantCulture) + 1)));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri("https://93.184.216.34/r0"), Cap);

        Assert.Equal(PoliteRefusal.RedirectRefused, result.Refusal);
        Assert.Equal(6, web.Requested.Count(u => u.AbsolutePath != "/robots.txt" && u.AbsolutePath.StartsWith("/r", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_body_over_the_cap_with_no_declared_length_is_refused_once_it_is_read_past_the_cap()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : StreamedPage(new string('x', 20)));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), 10);

        Assert.Equal(PoliteRefusal.TooLarge, result.Refusal);
        Assert.Empty(result.Body);
    }

    [Fact]
    public async Task A_body_declared_over_the_cap_is_refused_before_it_is_read()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : Reply(HttpStatusCode.OK, new string('x', 20)));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), 10);

        Assert.Equal(PoliteRefusal.TooLarge, result.Refusal);
        Assert.Contains("declares", result.Detail);
    }

    [Fact]
    public async Task A_non_2xx_answer_is_a_result_with_no_body()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : Reply(HttpStatusCode.NotFound, "missing"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.False(result.Refused);
        Assert.Equal(HttpStatusCode.NotFound, result.Status);
        Assert.Empty(result.Body);
    }

    [Fact]
    public async Task A_network_failure_is_a_result_not_an_exception()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : throw new HttpRequestException("connection reset"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.Unreachable, result.Refusal);
    }

    [Fact]
    public async Task A_host_that_does_not_answer_in_time_gives_a_timed_out_result()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : throw new TaskCanceledException("timed out"));

        var result = await NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.TimedOut, result.Refusal);
    }

    [Fact]
    public async Task Requests_to_one_host_start_at_least_the_minimum_gap_apart()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.OK, "User-agent: *\n")
            : Reply(HttpStatusCode.OK, "ok"));
        List<TimeSpan> waits = [];
        var fetcher = NewFetcher(web, new Clock(), waits);

        await fetcher.GetAsync(new Uri(Page), Cap);
        await fetcher.GetAsync(new Uri(Page + "2"), Cap);

        Assert.Equal(2, waits.Count);
        Assert.All(waits, wait => Assert.Equal(TimeSpan.FromSeconds(3), wait));
    }

    [Theory]
    [InlineData("User-agent: *\n", 3)]
    [InlineData("User-agent: *\nCrawl-delay: 1\n", 3)]
    [InlineData("User-agent: *\nCrawl-delay: 10\n", 10)]
    public async Task The_gap_is_the_crawl_delay_when_it_is_longer_than_the_minimum(string robots, int seconds)
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.OK, robots)
            : Reply(HttpStatusCode.OK, "ok"));
        List<TimeSpan> waits = [];

        await NewFetcher(web, new Clock(), waits).GetAsync(new Uri(Page), Cap);

        Assert.Equal(TimeSpan.FromSeconds(seconds), Assert.Single(waits));
    }

    [Fact]
    public async Task A_crawl_delay_over_the_maximum_is_refused_with_the_delay_asked_and_nothing_is_waited_for()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.OK, "User-agent: *\nCrawl-delay: 61\n")
            : Reply(HttpStatusCode.OK, "ok"));
        List<TimeSpan> waits = [];

        var result = await NewFetcher(web, new Clock(), waits).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.CrawlDelayTooLong, result.Refusal);
        Assert.Equal(TimeSpan.FromSeconds(61), result.CrawlDelayAsked);
        Assert.Empty(waits);
        Assert.DoesNotContain(web.Requested, u => u.AbsolutePath == "/timetable");
    }

    [Fact]
    public async Task With_a_three_second_maximum_a_61_second_delay_is_refused_and_a_3_second_one_is_the_gap()
    {
        var limit = new PoliteFetcherOptions { MaxCrawlDelay = TimeSpan.FromSeconds(3) };
        var refusedWeb = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.OK, "User-agent: *\nCrawl-delay: 61\n")
            : Reply(HttpStatusCode.OK, "ok"));

        var refused = await NewFetcher(refusedWeb, new Clock(), [], limit).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.CrawlDelayTooLong, refused.Refusal);
        Assert.Equal(TimeSpan.FromSeconds(61), refused.CrawlDelayAsked);

        var honouredWeb = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.OK, "User-agent: *\nCrawl-delay: 3\n")
            : Reply(HttpStatusCode.OK, "ok"));
        List<TimeSpan> waits = [];

        var honoured = await NewFetcher(honouredWeb, new Clock(), waits, limit).GetAsync(new Uri(Page), Cap);

        Assert.False(honoured.Refused);
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(waits));
    }

    [Fact]
    public async Task The_callers_cancellation_is_rethrown_and_not_turned_into_a_result()
    {
        var web = new ScriptedWeb(_ => Reply(HttpStatusCode.OK, "ok"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NewFetcher(web, new Clock(), []).GetAsync(new Uri(Page), Cap, cancellation.Token));
    }

    [Theory]
    [InlineData("https://busmaps.com/timetable")]
    [InlineData("https://tiles.busmaps.com/timetable")]
    [InlineData("https://busmaps.com./timetable")]
    public async Task A_blocked_host_a_subdomain_of_it_or_its_fully_qualified_name_is_refused_and_never_requested(string url)
    {
        var web = new ScriptedWeb(_ => Reply(HttpStatusCode.OK, "must not be reached"));

        var result = await NewFetcher(web, new Clock(), [], blockedHosts: ["busmaps.com"]).GetAsync(new Uri(url), Cap);

        Assert.Equal(PoliteRefusal.BlockedHost, result.Refusal);
        Assert.Empty(web.Requested);
    }

    [Fact]
    public async Task A_redirect_to_a_blocked_host_stops_before_that_hop_with_the_host_named_in_the_reason()
    {
        var web = new ScriptedWeb(uri => uri switch
        {
            { Host: "93.184.216.34", AbsolutePath: "/robots.txt" } => Reply(HttpStatusCode.NotFound, string.Empty),
            { Host: "93.184.216.34", AbsolutePath: "/timetable" } => RedirectTo("https://busmaps.com/landing"),
            _ => Reply(HttpStatusCode.OK, "must not be reached"),
        });

        var result = await NewFetcher(web, new Clock(), [], blockedHosts: ["busmaps.com"]).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.BlockedHost, result.Refusal);
        Assert.Contains("busmaps.com", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(web.Requested, u => u.Host == "busmaps.com");
        Assert.Equal(new[] { "https://93.184.216.34/robots.txt", Page }, web.Requested.Select(u => u.AbsoluteUri).ToArray());
    }

    [Fact]
    public async Task A_robots_txt_redirected_to_a_blocked_host_is_not_requested_and_the_page_is_refused()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? RedirectTo("https://busmaps.com/robots.txt")
            : Reply(HttpStatusCode.OK, "must not be reached"));

        var result = await NewFetcher(web, new Clock(), [], blockedHosts: ["busmaps.com"]).GetAsync(new Uri(Page), Cap);

        Assert.Equal(PoliteRefusal.RobotsDisallowed, result.Refusal);
        Assert.Contains("busmaps.com", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(web.Requested, u => u.Host == "busmaps.com");
        Assert.DoesNotContain(web.Requested, u => u.AbsolutePath == "/timetable");
    }

    [Fact]
    public async Task With_no_blocked_list_a_page_is_read_as_before()
    {
        var web = new ScriptedWeb(uri => uri.AbsolutePath == "/robots.txt"
            ? Reply(HttpStatusCode.NotFound, string.Empty)
            : Reply(HttpStatusCode.OK, "read"));

        var none = await NewFetcher(web, new Clock(), [], blockedHosts: null).GetAsync(new Uri(Page), Cap);
        var empty = await NewFetcher(web, new Clock(), [], blockedHosts: []).GetAsync(new Uri(Page), Cap);

        Assert.Equal("read", none.Text);
        Assert.Equal("read", empty.Text);
    }

    [Theory]
    [InlineData("busmaps.com", true)]
    [InlineData("BusMaps.COM", true)]
    [InlineData("tiles.busmaps.com", true)]
    [InlineData("busmaps.com.", true)]
    [InlineData("notbusmaps.com", false)]
    [InlineData("busmaps.com.example.org", false)]
    [InlineData("example.org", false)]
    public void A_host_is_blocked_when_it_is_an_entry_or_under_one_ignoring_case(string host, bool blocked)
    {
        Assert.Equal(blocked, PoliteFetcher.IsBlockedHost(host, ["busmaps.com"]));
    }

    [Fact]
    public void An_empty_blocked_list_blocks_no_host()
    {
        Assert.False(PoliteFetcher.IsBlockedHost("busmaps.com", []));
    }

    [Fact]
    public void An_entry_written_with_a_trailing_dot_blocks_the_same_host()
    {
        Assert.True(PoliteFetcher.IsBlockedHost("tiles.busmaps.com", ["busmaps.com."]));
        Assert.True(PoliteFetcher.IsBlockedHost("busmaps.com", ["busmaps.com."]));
    }

    private static PoliteFetcher NewFetcher(
        ScriptedWeb web,
        Clock clock,
        List<TimeSpan> waits,
        PoliteFetcherOptions? limits = null,
        IReadOnlyList<string>? blockedHosts = null) => new(
        new StubFactory(web),
        "bot",
        Settings(limits, blockedHosts),
        clock,
        (delay, _) =>
        {
            waits.Add(delay);
            clock.Advance(delay);
            return Task.CompletedTask;
        });

    /// <summary>The options a test needs: the crawler's name and info page, the caller's limits and blocked hosts.</summary>
    private static PoliteFetcherOptions Settings(PoliteFetcherOptions? limits, IReadOnlyList<string>? blockedHosts) => new()
    {
        ProductToken = "ExampleBot",
        BotInfoUrl = "https://example.test/bot",
        MaxCrawlDelay = limits?.MaxCrawlDelay ?? TimeSpan.FromSeconds(60),
        BlockedHosts = blockedHosts ?? [],
    };

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage StreamedPage(string text) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text))) };

    private static HttpResponseMessage RedirectTo(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
        response.Headers.Location = new Uri(location, UriKind.Absolute);
        return response;
    }

    /// <summary>Answers each request from a script, records the URLs asked for, and turns a thrown failure into a failed send.</summary>
    private sealed class ScriptedWeb(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            try
            {
                return Task.FromResult(respond(request.RequestUri!));
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>A clock the test moves: a wait advances it, so the gap is measured on it.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
