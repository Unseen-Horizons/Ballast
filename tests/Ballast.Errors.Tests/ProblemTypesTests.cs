// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

namespace Ballast.Errors.Tests;

public class ProblemTypesTests
{
    [Theory]
    [InlineData(400, "https://tools.ietf.org/html/rfc9110#section-15.5.1")]
    [InlineData(401, "https://tools.ietf.org/html/rfc9110#section-15.5.2")]
    [InlineData(402, "https://tools.ietf.org/html/rfc9110#section-15.5.3")]
    [InlineData(403, "https://tools.ietf.org/html/rfc9110#section-15.5.4")]
    [InlineData(404, "https://tools.ietf.org/html/rfc9110#section-15.5.5")]
    [InlineData(405, "https://tools.ietf.org/html/rfc9110#section-15.5.6")]
    [InlineData(406, "https://tools.ietf.org/html/rfc9110#section-15.5.7")]
    [InlineData(407, "https://tools.ietf.org/html/rfc9110#section-15.5.8")]
    [InlineData(408, "https://tools.ietf.org/html/rfc9110#section-15.5.9")]
    [InlineData(409, "https://tools.ietf.org/html/rfc9110#section-15.5.10")]
    [InlineData(410, "https://tools.ietf.org/html/rfc9110#section-15.5.11")]
    [InlineData(411, "https://tools.ietf.org/html/rfc9110#section-15.5.12")]
    [InlineData(412, "https://tools.ietf.org/html/rfc9110#section-15.5.13")]
    [InlineData(413, "https://tools.ietf.org/html/rfc9110#section-15.5.14")]
    [InlineData(414, "https://tools.ietf.org/html/rfc9110#section-15.5.15")]
    [InlineData(415, "https://tools.ietf.org/html/rfc9110#section-15.5.16")]
    [InlineData(416, "https://tools.ietf.org/html/rfc9110#section-15.5.17")]
    [InlineData(417, "https://tools.ietf.org/html/rfc9110#section-15.5.18")]
    [InlineData(421, "https://tools.ietf.org/html/rfc9110#section-15.5.20")]
    [InlineData(422, "https://tools.ietf.org/html/rfc9110#section-15.5.21")]
    [InlineData(426, "https://tools.ietf.org/html/rfc9110#section-15.5.22")]
    [InlineData(500, "https://tools.ietf.org/html/rfc9110#section-15.6.1")]
    [InlineData(501, "https://tools.ietf.org/html/rfc9110#section-15.6.2")]
    [InlineData(502, "https://tools.ietf.org/html/rfc9110#section-15.6.3")]
    [InlineData(503, "https://tools.ietf.org/html/rfc9110#section-15.6.4")]
    [InlineData(504, "https://tools.ietf.org/html/rfc9110#section-15.6.5")]
    [InlineData(505, "https://tools.ietf.org/html/rfc9110#section-15.6.6")]
    public void Each_status_that_rfc9110_defines_gives_its_section(int status, string type) =>
        Assert.Equal(type, ProblemTypes.For(status));

    [Fact]
    public void Too_many_requests_is_the_rfc6585_section()
    {
        Assert.Equal("https://tools.ietf.org/html/rfc6585#section-4", ProblemTypes.For(429));
    }

    [Theory]
    [InlineData(418)]
    [InlineData(419)]
    [InlineData(420)]
    [InlineData(427)]
    [InlineData(428)]
    [InlineData(499)]
    [InlineData(506)]
    [InlineData(599)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(200)]
    public void A_status_without_a_section_is_about_blank(int status) =>
        Assert.Equal("about:blank", ProblemTypes.For(status));

    [Fact]
    public void The_403_address_is_the_forbidden_section_not_payment_required()
    {
        Assert.NotEqual(ProblemTypes.For(402), ProblemTypes.For(403));
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.4", ProblemTypes.For(403));
    }
}
