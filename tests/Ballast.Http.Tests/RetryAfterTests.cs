// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;

namespace Ballast.Http.Tests;

/// <summary>
/// Retry-After in both forms, ported from the waits GetThere's Overpass client and Microsoft Graph client read (<c>OverpassClientTests</c>
/// and <c>MicrosoftGraphClientTests</c>): a delta is the wait, a date is the wait until it, a wait over the maximum is cut, and a date
/// that has passed is zero.
/// </summary>
public class RetryAfterTests
{
    private static readonly TimeSpan Sixty = TimeSpan.FromSeconds(60);

    [Fact]
    public void A_delta_in_seconds_is_the_wait()
    {
        var response = WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)));

        Assert.Equal(TimeSpan.FromSeconds(5), RetryAfter.Read(response, Sixty));
    }

    [Fact]
    public void A_delta_over_the_maximum_is_capped_at_the_maximum()
    {
        var response = WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(600)));

        Assert.Equal(Sixty, RetryAfter.Read(response, Sixty));
    }

    [Fact]
    public void A_date_waits_until_that_date_measured_from_the_clock()
    {
        var clock = new Clock();
        var response = WithRetryAfter(new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(20)));

        Assert.Equal(TimeSpan.FromSeconds(20), RetryAfter.Read(response, Sixty, clock));
    }

    [Fact]
    public void A_date_far_away_is_capped_at_the_maximum()
    {
        var clock = new Clock();
        var response = WithRetryAfter(new RetryConditionHeaderValue(clock.GetUtcNow().AddHours(1)));

        Assert.Equal(Sixty, RetryAfter.Read(response, Sixty, clock));
    }

    [Fact]
    public void A_date_that_has_passed_gives_zero_so_the_request_may_go_now()
    {
        var clock = new Clock();
        var response = WithRetryAfter(new RetryConditionHeaderValue(clock.GetUtcNow().AddMinutes(-5)));

        Assert.Equal(TimeSpan.Zero, RetryAfter.Read(response, Sixty, clock));
    }

    [Fact]
    public void Without_a_retry_after_header_the_answer_is_null_so_the_caller_uses_its_own_wait()
    {
        Assert.Null(RetryAfter.Read(new HttpResponseMessage(HttpStatusCode.TooManyRequests), Sixty));
    }

    [Fact]
    public void A_zero_maximum_gives_zero_for_any_wait()
    {
        var response = WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(3)));

        Assert.Equal(TimeSpan.Zero, RetryAfter.Read(response, TimeSpan.Zero));
    }

    [Fact]
    public void A_negative_maximum_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetryAfter.Read(new HttpResponseMessage(HttpStatusCode.TooManyRequests), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void A_null_response_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => RetryAfter.Read(null!, Sixty));
    }

    private static HttpResponseMessage WithRetryAfter(RetryConditionHeaderValue value)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = value;
        return response;
    }
}
