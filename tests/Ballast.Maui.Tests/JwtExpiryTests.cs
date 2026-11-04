// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Ballast.Maui.Auth;

namespace Ballast.Maui.Tests;

/// <summary>The end time read from a token's <c>exp</c> claim, with its unsigned payload, and the five-minute test that uses it.</summary>
public class JwtExpiryTests
{
    private static string Encode(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Jwt(string payload) => $"{Encode("{\"alg\":\"none\"}")}.{Encode(payload)}.signature";

    [Fact]
    public void The_exp_claim_is_the_end_of_the_token()
    {
        var token = Jwt("{\"exp\":1800000000}");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000), JwtExpiry.Read(token));
    }

    [Theory]
    [InlineData("opaque")]
    [InlineData("a.!!!.c")]
    [InlineData("a.bm90IGpzb24.c")]
    public void A_token_without_a_readable_payload_has_no_end(string token) =>
        Assert.Null(JwtExpiry.Read(token));

    [Fact]
    public void A_payload_without_an_exp_claim_has_no_end() =>
        Assert.Null(JwtExpiry.Read(Jwt("{\"sub\":\"rider\"}")));

    [Fact]
    public void A_token_ending_within_the_window_is_due_for_renewal()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var token = Jwt($"{{\"exp\":{now.AddMinutes(4).ToUnixTimeSeconds()}}}");

        Assert.True(JwtExpiry.EndsWithin(token, now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void A_token_with_more_than_the_window_left_is_not_due()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var token = Jwt($"{{\"exp\":{now.AddMinutes(10).ToUnixTimeSeconds()}}}");

        Assert.False(JwtExpiry.EndsWithin(token, now, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("\"text\"")]
    [InlineData("1800000000")]
    [InlineData("null")]
    public void A_payload_that_is_json_but_not_an_object_has_no_end(string payload)
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var token = Jwt(payload);

        Assert.Null(JwtExpiry.Read(token));
        Assert.False(JwtExpiry.EndsWithin(token, now, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData("{\"exp\":\"1800000000\"}")]
    [InlineData("{\"exp\":null}")]
    [InlineData("{\"exp\":true}")]
    [InlineData("{\"exp\":[1800000000]}")]
    [InlineData("{\"exp\":1800000000.5}")]
    public void An_exp_that_is_not_a_whole_number_has_no_end(string payload)
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var token = Jwt(payload);

        Assert.Null(JwtExpiry.Read(token));
        Assert.False(JwtExpiry.EndsWithin(token, now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void An_exp_too_large_to_be_a_date_has_no_end() =>
        Assert.Null(JwtExpiry.Read(Jwt("{\"exp\":9223372036854775807}")));

    [Fact]
    public void A_token_already_past_its_end_is_due()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var token = Jwt($"{{\"exp\":{now.AddMinutes(-1).ToUnixTimeSeconds()}}}");

        Assert.True(JwtExpiry.EndsWithin(token, now, TimeSpan.FromMinutes(5)));
    }
}
