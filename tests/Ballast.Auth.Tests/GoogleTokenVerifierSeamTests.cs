// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

using Google.Apis.Auth;

using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.Auth.Tests;

/// <summary>
/// The checks <see cref="GoogleIdTokenValidator"/> makes, driven through the verifier seam: a fake answers for Google, so the audience, the
/// issuer, the expiry and the email-verified flag are tested without the network.
/// </summary>
public class GoogleTokenVerifierSeamTests
{
    private const string ClientId = "test-client.apps.googleusercontent.com";

    private static GoogleIdTokenValidator Validator(IGoogleTokenVerifier verifier) =>
        new(new AuthOptions(), new AuthGoogleOptions { WebClientId = ClientId }, verifier, NullLogger<GoogleIdTokenValidator>.Instance);

    private static GoogleJsonWebSignature.Payload Payload(
        string issuer = "https://accounts.google.com",
        string audience = ClientId,
        long? expires = null,
        bool emailVerified = true) => new()
        {
            Subject = "google-sub-7",
            Email = "rider@example.com",
            EmailVerified = emailVerified,
            Name = "Ann Rider",
            Issuer = issuer,
            Audience = audience,
            ExpirationTimeSeconds = expires ?? DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
        };

    private static FakeVerifier Answering(GoogleJsonWebSignature.Payload payload) => new(_ => payload);

    [Fact]
    public async Task The_verifier_is_asked_for_this_apps_audience_and_no_other()
    {
        var fake = Answering(Payload());

        await Validator(fake).ValidateAsync("token");

        Assert.Equal(ClientId, Assert.Single(fake.Audiences!));
    }

    [Fact]
    public async Task A_payload_from_google_for_this_app_becomes_the_identity_it_vouches_for()
    {
        var identity = await Validator(Answering(Payload())).ValidateAsync("token");

        Assert.Equal(new GoogleIdentity("google-sub-7", "rider@example.com", true, "Ann Rider"), identity);
    }

    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("https://accounts.google.com")]
    public async Task Both_of_googles_issuer_forms_are_accepted(string issuer)
    {
        var identity = await Validator(Answering(Payload(issuer: issuer))).ValidateAsync("token");

        Assert.NotNull(identity);
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("accounts.google.com.evil.example")]
    [InlineData("")]
    public async Task A_token_from_another_issuer_is_refused(string issuer)
    {
        var identity = await Validator(Answering(Payload(issuer: issuer))).ValidateAsync("token");

        Assert.Null(identity);
    }

    [Fact]
    public async Task A_token_with_no_issuer_is_refused()
    {
        var payload = Payload();
        payload.Issuer = null;

        Assert.Null(await Validator(new FakeVerifier(_ => payload)).ValidateAsync("token"));
    }

    [Fact]
    public async Task A_token_for_another_app_is_refused()
    {
        var identity = await Validator(Answering(Payload(audience: "someone-elses.apps.googleusercontent.com"))).ValidateAsync("token");

        Assert.Null(identity);
    }

    [Fact]
    public async Task A_token_past_its_expiry_is_refused()
    {
        var expired = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();

        Assert.Null(await Validator(Answering(Payload(expires: expired))).ValidateAsync("token"));
    }

    [Fact]
    public async Task A_token_with_no_expiry_is_refused()
    {
        var payload = Payload();
        payload.ExpirationTimeSeconds = null;

        Assert.Null(await Validator(new FakeVerifier(_ => payload)).ValidateAsync("token"));
    }

    [Fact]
    public async Task An_unverified_google_address_comes_through_with_the_flag_false_for_the_sign_in_to_refuse()
    {
        var identity = await Validator(Answering(Payload(emailVerified: false))).ValidateAsync("token");

        Assert.NotNull(identity);
        Assert.False(identity.EmailVerified);
    }

    [Fact]
    public async Task A_token_the_verifier_rejects_is_refused_as_not_a_google_token()
    {
        var validator = Validator(new FakeVerifier(_ => throw new InvalidJwtException("Signature is not valid.")));

        Assert.Null(await validator.ValidateAsync("token"));
    }

    [Fact]
    public async Task A_verifier_that_cannot_reach_google_is_a_503_not_a_bad_token()
    {
        var validator = Validator(new FakeVerifier(_ => throw new HttpRequestException("no route to host")));

        var error = await Assert.ThrowsAsync<AppException>(() => validator.ValidateAsync("token"));

        Assert.Equal("GOOGLE_UNREACHABLE", error.Code);
        Assert.Equal(503, error.StatusCode);
    }

    /// <summary>A verifier that answers for Google, and records the audience it was asked for.</summary>
    private sealed class FakeVerifier(Func<string, GoogleJsonWebSignature.Payload> answer) : IGoogleTokenVerifier
    {
        /// <summary>The audience list of each call, in order.</summary>
        public IEnumerable<string>? Audiences { get; private set; }

        public Task<GoogleJsonWebSignature.Payload> VerifyAsync(string idToken, GoogleJsonWebSignature.ValidationSettings settings)
        {
            Audiences = settings.Audience;
            return Task.FromResult(answer(idToken));
        }
    }
}
