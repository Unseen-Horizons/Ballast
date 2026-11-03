// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.Maui.Auth;

namespace Ballast.Maui.Tests;

/// <summary>
/// Only a refused refresh token ends the session. A 429, a 5xx during a deploy, a captive portal or no network is no verdict on
/// the token, so the rider stays signed in and the stored tokens are not touched. The wiring that acts on the outcome is pinned by
/// <see cref="BearerTokenHandlerTests"/>.
/// </summary>
public class RefreshOutcomeRulesTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, RefreshOutcome.Refreshed)]
    [InlineData(HttpStatusCode.Unauthorized, RefreshOutcome.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, RefreshOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, RefreshOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, RefreshOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, RefreshOutcome.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, RefreshOutcome.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, RefreshOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, RefreshOutcome.Unavailable)]
    public void A_refresh_reply_is_judged_by_its_status(HttpStatusCode status, RefreshOutcome expected) =>
        Assert.Equal(expected, RefreshOutcomeRules.FromStatus(status));

    [Theory]
    [InlineData(RefreshOutcome.Rejected, true)]
    [InlineData(RefreshOutcome.Unavailable, false)]
    [InlineData(RefreshOutcome.Refreshed, false)]
    public void Only_a_rejected_refresh_signs_the_rider_out(RefreshOutcome outcome, bool expected) =>
        Assert.Equal(expected, RefreshOutcomeRules.ShouldSignOut(outcome));
}
