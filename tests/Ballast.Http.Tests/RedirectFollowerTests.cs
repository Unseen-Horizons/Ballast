// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Ballast.Http.Tests;

/// <summary>
/// The redirect rules, one test per rule and per reason. The scenarios come from GetThere's price-feed download tests and feed
/// source tests, and the hosts are public address literals, so nothing resolves a name. Every refused hop must never be sent: the
/// assertions count what the handler saw.
/// </summary>
public class RedirectFollowerTests
{
    private const string Origin = "https://93.184.216.34/feed.zip";
    private const string SameHost = "https://93.184.216.34/v2/feed.zip";
    private const string OffHost = "https://1.1.1.1/feed.zip";
    private static readonly byte[] Body = Encoding.ASCII.GetBytes("PK-fake-feed-body");

    [Fact]
    public async Task A_plain_redirect_is_followed_and_no_credential_is_sent()
    {
        var web = new ScriptedWeb(request => request.RequestUri!.Host == "93.184.216.34"
            ? Answer.RedirectTo(OffHost)
            : Answer.Ok(Body));

        using var response = await Follower(web).SendAsync(new RedirectRequest(new Uri(Origin)));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(2, web.Requests.Count);
        Assert.All(web.Requests, seen => Assert.Null(seen.Authorization));
    }

    [Fact]
    public async Task A_credential_is_sent_on_every_same_host_hop()
    {
        var web = new ScriptedWeb(request => request.RequestUri!.AbsolutePath == "/feed.zip"
            ? Answer.RedirectTo(SameHost)
            : Answer.Ok(Body));

        using var response = await Follower(web).SendAsync(new RedirectRequest(new Uri(Origin)) { Authorization = Basic("user:pass") });

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(2, web.Requests.Count);
        Assert.All(web.Requests, seen => Assert.Equal(Basic("user:pass").ToString(), seen.Authorization));
    }

    [Fact]
    public async Task A_credential_redirect_to_another_host_is_refused_before_anything_is_sent()
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo(OffHost));

        var ex = await AssertRefusedAsync(web, HttpSafetyReason.CredentialOffHost, new RedirectRequest(new Uri(Origin)) { Authorization = Basic("user:pass") });

        Assert.Single(web.Requests);
        Assert.Equal("https://93.184.216.34/feed.zip", ex.RedactedUrl);
    }

    [Fact]
    public async Task A_credential_redirect_from_https_to_http_is_refused_before_anything_is_sent()
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo("http://93.184.216.34/feed.zip"));

        await AssertRefusedAsync(web, HttpSafetyReason.SchemeDowngrade, new RedirectRequest(new Uri(Origin)) { Authorization = Basic("user:pass") });

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_credential_redirect_to_another_port_is_refused_before_anything_is_sent()
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo("https://93.184.216.34:8443/feed.zip"));

        await AssertRefusedAsync(web, HttpSafetyReason.CredentialOtherPort, new RedirectRequest(new Uri(Origin)) { Authorization = Basic("user:pass") });

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_credential_on_an_http_address_is_refused_before_any_request()
    {
        var web = new ScriptedWeb(_ => Answer.Ok(Body));

        await AssertRefusedAsync(web, HttpSafetyReason.CredentialNotHttps,
            new RedirectRequest(new Uri("http://93.184.216.34/feed.zip")) { Authorization = Basic("user:pass") });

        Assert.Empty(web.Requests);
    }

    [Fact]
    public async Task A_redirect_into_the_private_network_is_refused_at_its_hop()
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo("https://10.0.0.1/feed.zip"));

        await AssertRefusedAsync(web, HttpSafetyReason.NonPublicAddress, new RedirectRequest(new Uri(Origin)));

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_redirect_from_https_to_http_is_refused_even_without_a_credential()
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo("http://1.1.1.1/feed.zip"));

        await AssertRefusedAsync(web, HttpSafetyReason.SchemeDowngrade, new RedirectRequest(new Uri(Origin)));

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_redirect_that_embeds_credentials_is_refused_and_the_secret_is_not_named()
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo("https://u:secret@1.1.1.1/feed.zip"));

        var ex = await AssertRefusedAsync(web, HttpSafetyReason.UserInfoInUrl, new RedirectRequest(new Uri(Origin)));

        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", ex.RedactedUrl ?? string.Empty, StringComparison.Ordinal);
        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task Five_redirects_are_followed_and_a_sixth_is_refused()
    {
        var accepted = RedirectChain(redirects: 5);
        using var response = await Follower(accepted).SendAsync(new RedirectRequest(new Uri("https://93.184.216.34/r0")));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(6, accepted.Requests.Count);

        var refused = RedirectChain(redirects: 6);
        var ex = await AssertRefusedAsync(refused, HttpSafetyReason.TooManyRedirects, new RedirectRequest(new Uri("https://93.184.216.34/r0")));

        Assert.Equal(6, refused.Requests.Count);
        Assert.Equal("https://93.184.216.34/r0", ex.RedactedUrl);
    }

    [Fact]
    public async Task The_redirect_limit_is_an_option_and_zero_refuses_the_first_redirect()
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo(SameHost));

        await AssertRefusedAsync(web, HttpSafetyReason.TooManyRedirects,
            new RedirectRequest(new Uri(Origin)), new RedirectFollowerOptions { MaxRedirects = 0 });

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_url_with_user_and_password_is_refused_before_any_request()
    {
        var web = new ScriptedWeb(_ => Answer.Ok(Body));

        var ex = await AssertRefusedAsync(web, HttpSafetyReason.UserInfoInUrl, new RedirectRequest(new Uri("https://user:secret@93.184.216.34/feed.zip")));

        Assert.Empty(web.Requests);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
        Assert.Equal("https://93.184.216.34/feed.zip", ex.RedactedUrl);
    }

    [Fact]
    public async Task A_plain_http_address_is_refused_when_https_is_required()
    {
        var web = new ScriptedWeb(_ => Answer.Ok(Body));

        await AssertRefusedAsync(web, HttpSafetyReason.SchemeNotAllowed, new RedirectRequest(new Uri("http://93.184.216.34/feed.zip")));

        Assert.Empty(web.Requests);
    }

    [Fact]
    public async Task A_non_web_scheme_is_refused_as_an_invalid_url_before_any_request()
    {
        var web = new ScriptedWeb(_ => Answer.Ok(Body));

        await AssertRefusedAsync(web, HttpSafetyReason.InvalidUrl, new RedirectRequest(new Uri("ftp://93.184.216.34/feed.zip")));

        Assert.Empty(web.Requests);
    }

    [Fact]
    public async Task A_redirect_to_a_non_web_scheme_is_refused()
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo("ftp://93.184.216.34/feed.zip"));

        await AssertRefusedAsync(web, HttpSafetyReason.SchemeNotAllowed, new RedirectRequest(new Uri(Origin)));

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task When_https_is_not_required_plain_http_is_allowed_but_an_https_address_never_redirects_to_http()
    {
        var options = new RedirectFollowerOptions { RequireHttps = false };
        var plain = new ScriptedWeb(_ => Answer.Ok(Body));
        using (var response = await Follower(plain, options).SendAsync(new RedirectRequest(new Uri("http://93.184.216.34/feed.zip"))))
        {
            Assert.True(response.IsSuccessStatusCode);
        }

        var upgrade = new ScriptedWeb(request => request.RequestUri!.Scheme == "http"
            ? Answer.RedirectTo("https://93.184.216.34/feed.zip")
            : Answer.Ok(Body));
        using (var response = await Follower(upgrade, options).SendAsync(new RedirectRequest(new Uri("http://93.184.216.34/feed.zip"))))
        {
            Assert.True(response.IsSuccessStatusCode);
        }

        var downgrade = new ScriptedWeb(_ => Answer.RedirectTo("http://93.184.216.34/feed.zip"));
        await AssertRefusedAsync(downgrade, HttpSafetyReason.SchemeDowngrade, new RedirectRequest(new Uri(Origin)), options);
    }

    [Fact]
    public async Task A_redirect_without_a_location_is_refused()
    {
        var web = new ScriptedWeb(_ => new HttpResponseMessage(HttpStatusCode.Found));

        await AssertRefusedAsync(web, HttpSafetyReason.MissingLocation, new RedirectRequest(new Uri(Origin)));

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_location_that_is_not_a_url_is_refused_as_a_location_that_cannot_be_used()
    {
        var web = new ScriptedWeb(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.TryAddWithoutValidation("Location", "http://[bad");
            return response;
        });

        await AssertRefusedAsync(web, HttpSafetyReason.InvalidLocation, new RedirectRequest(new Uri(Origin)));

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_relative_location_is_resolved_against_the_address_that_answered()
    {
        var web = new ScriptedWeb(request => request.RequestUri!.AbsolutePath == "/a/b"
            ? Answer.RedirectTo("../c", HttpStatusCode.Found)
            : Answer.Ok(Body));

        using var response = await Follower(web).SendAsync(new RedirectRequest(new Uri("https://93.184.216.34/a/b")));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("https://93.184.216.34/c", web.Requests[1].Url.AbsoluteUri);
    }

    [Fact]
    public async Task A_see_other_becomes_a_get_without_a_body()
    {
        var web = new ScriptedWeb(request => request.Method == HttpMethod.Post
            ? Answer.RedirectTo("https://93.184.216.34/done", HttpStatusCode.SeeOther)
            : Answer.Ok(Body));

        using var response = await Follower(web).SendAsync(PostWithBody("payload"));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(HttpMethod.Get, web.Requests[1].Method);
        Assert.Null(web.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    public async Task A_moved_or_found_redirect_of_a_post_becomes_a_get_without_a_body(HttpStatusCode status)
    {
        var web = new ScriptedWeb(request => request.Method == HttpMethod.Post
            ? Answer.RedirectTo("https://93.184.216.34/done", status)
            : Answer.Ok(Body));

        using var response = await Follower(web).SendAsync(PostWithBody("payload"));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(HttpMethod.Get, web.Requests[1].Method);
        Assert.Null(web.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task A_307_or_308_keeps_the_method_and_resends_the_body_to_the_same_host(HttpStatusCode status)
    {
        var web = new ScriptedWeb(request => request.RequestUri!.AbsolutePath == "/feed.zip"
            ? Answer.RedirectTo(SameHost, status)
            : Answer.Ok(Body));

        using var response = await Follower(web).SendAsync(PostWithBody("payload"));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(HttpMethod.Post, web.Requests[1].Method);
        Assert.Equal("payload", web.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task A_307_or_308_body_redirected_to_another_host_is_refused_before_it_is_sent(HttpStatusCode status)
    {
        var web = new ScriptedWeb(_ => Answer.RedirectTo(OffHost, status));

        await AssertRefusedAsync(web, HttpSafetyReason.BodyOffHost, PostWithBody("payload"));

        Assert.Single(web.Requests);
    }

    [Fact]
    public async Task A_get_redirected_to_another_host_is_followed_because_it_carries_no_body()
    {
        var web = new ScriptedWeb(request => request.RequestUri!.Host == "93.184.216.34"
            ? Answer.RedirectTo(OffHost, HttpStatusCode.TemporaryRedirect)
            : Answer.Ok(Body));

        using var response = await Follower(web).SendAsync(new RedirectRequest(new Uri(Origin)));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(2, web.Requests.Count);
    }

    [Fact]
    public async Task A_request_body_is_built_again_for_each_hop_that_sends_one()
    {
        var builds = 0;
        var web = new ScriptedWeb(request => request.RequestUri!.AbsolutePath == "/feed.zip"
            ? Answer.RedirectTo(SameHost, HttpStatusCode.TemporaryRedirect)
            : Answer.Ok(Body));
        var request = new RedirectRequest(new Uri(Origin))
        {
            Method = HttpMethod.Post,
            Body = () =>
            {
                builds++;
                return new StringContent("payload", Encoding.UTF8, "text/plain");
            },
        };

        using var response = await Follower(web).SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task A_connect_time_refusal_inside_the_client_is_thrown_as_its_own_reason()
    {
        var web = new ScriptedWeb(_ => throw new HttpRequestException(
            "connect refused", new HttpSafetyException(HttpSafetyReason.NonPublicAddress, "resolves to a non-public address.")));

        await AssertRefusedAsync(web, HttpSafetyReason.NonPublicAddress, new RedirectRequest(new Uri(Origin)));
    }

    [Fact]
    public async Task A_client_timeout_is_thrown_as_the_client_throws_it_and_is_not_a_refusal()
    {
        var web = new ScriptedWeb(_ => throw new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<TaskCanceledException>(() => Follower(web).SendAsync(new RedirectRequest(new Uri(Origin))));
    }

    [Fact]
    public async Task A_network_failure_is_an_http_request_exception_and_not_a_refusal()
    {
        var web = new ScriptedWeb(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Follower(web).SendAsync(new RedirectRequest(new Uri(Origin))));

        Assert.Equal("connection reset", ex.Message);
    }

    [Fact]
    public async Task A_caller_cancellation_is_thrown_as_cancelled()
    {
        var web = new ScriptedWeb(_ => Answer.Ok(Body));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Follower(web).SendAsync(new RedirectRequest(new Uri(Origin)), cancellation.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, true)]
    [InlineData(HttpStatusCode.Found, true)]
    [InlineData(HttpStatusCode.SeeOther, true)]
    [InlineData(HttpStatusCode.TemporaryRedirect, true)]
    [InlineData(HttpStatusCode.PermanentRedirect, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NotModified, false)]
    [InlineData(HttpStatusCode.MultipleChoices, false)]
    [InlineData(HttpStatusCode.UseProxy, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void Only_the_five_redirect_statuses_are_followed(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, RedirectFollower.IsRedirect(status));
    }

    [Fact]
    public void A_negative_redirect_limit_is_refused_at_construction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RedirectFollower(new StubFactory(new ScriptedWeb(_ => Answer.Ok(Body))), "client", new RedirectFollowerOptions { MaxRedirects = -1 }));
    }

    [Fact]
    public void A_blank_client_name_is_refused_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new RedirectFollower(new StubFactory(new ScriptedWeb(_ => Answer.Ok(Body))), " "));
    }

    [Fact]
    public async Task A_request_with_no_url_is_an_argument_error_and_nothing_is_sent()
    {
        var web = new ScriptedWeb(_ => Answer.Ok(Body));

        await Assert.ThrowsAsync<ArgumentNullException>(() => Follower(web).SendAsync(new RedirectRequest(null!)));
        Assert.Empty(web.Requests);
    }

    private static RedirectFollower Follower(ScriptedWeb web, RedirectFollowerOptions? options = null) =>
        new(new StubFactory(web), "client", options);

    private static AuthenticationHeaderValue Basic(string credential) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credential)));

    private static RedirectRequest PostWithBody(string text) => new(new Uri(Origin))
    {
        Method = HttpMethod.Post,
        Body = () => new StringContent(text, Encoding.UTF8, "text/plain"),
    };

    /// <summary>A chain of <paramref name="redirects"/> redirects, then the feed. Each redirect goes to the next numbered path.</summary>
    private static ScriptedWeb RedirectChain(int redirects) => new(request =>
    {
        var index = int.Parse(request.RequestUri!.AbsolutePath[2..], System.Globalization.CultureInfo.InvariantCulture);
        return index < redirects
            ? Answer.RedirectTo("https://93.184.216.34/r" + (index + 1), HttpStatusCode.Found)
            : Answer.Ok(Body);
    });

    /// <summary>Sends the request, which must be refused for <paramref name="reason"/>. Returns the refusal.</summary>
    private static async Task<HttpSafetyException> AssertRefusedAsync(
        ScriptedWeb web, HttpSafetyReason reason, RedirectRequest request, RedirectFollowerOptions? options = null)
    {
        var ex = await Assert.ThrowsAsync<HttpSafetyException>(() => Follower(web, options).SendAsync(request));
        Assert.Equal(reason, ex.Reason);
        return ex;
    }
}
