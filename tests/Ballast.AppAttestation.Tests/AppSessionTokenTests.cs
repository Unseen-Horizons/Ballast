// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;
using System.Text;

using Ballast.AppAttestation.Tests.Support;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.AppAttestation.Tests;

/// <summary>The session token: what it carries, and what it refuses to be mistaken for.</summary>
public class AppSessionTokenTests
{
    private const string SessionIssuer = AppAttestationTestHost.Issuer;
    private const string SessionAudience = AppAttestationTestHost.Audience;

    private static AppAttestationSetup Setup() => new()
    {
        PackageName = AppAttestationTestHost.Package,
        Issuer = SessionIssuer,
        Audience = SessionAudience,
        HeaderName = AppAttestationTestHost.Header,
        CookieName = AppAttestationTestHost.Cookie,
        RedirectPrefix = AppAttestationTestHost.Prefix,
        SessionRequiredDetail = "detail",
        SessionRequiredCode = AppAttestationCodes.SessionRequired,
        AttestationFailedCode = AppAttestationCodes.AttestationFailed,
        AttestationUnavailableCode = AppAttestationCodes.AttestationUnavailable,
    };

    private static AppSessionTokenService Issuer(FakeClock clock, string key = AppAttestationTestHost.SessionKey, int hours = 24) =>
        new(Options.Create(new AppSessionOptions { Key = key, LifetimeHours = hours }), Setup(), clock);

    private static AppSessionValidator Validator(FakeClock clock, string key = AppAttestationTestHost.SessionKey, bool acceptUnattested = true) =>
        new(key, SessionIssuer, SessionAudience, acceptUnattested, clock);

    [Theory]
    [InlineData("play", "android")]
    [InlineData("dev", "android")]
    [InlineData("none", "windows")]
    public async Task The_attestation_and_platform_round_trip(string attestation, string platform)
    {
        var clock = new FakeClock();
        var issued = Issuer(clock).Issue(attestation, platform);

        var result = await Validator(clock).ValidateAsync(issued.Token);

        Assert.NotNull(result.Session);
        Assert.Equal(attestation, result.Session!.Attestation);
        Assert.Equal(platform, result.Session.Platform);
        Assert.Equal(issued.SessionId, result.Session.SessionId);
    }

    [Fact]
    public async Task A_session_lasts_twenty_four_hours_by_default()
    {
        var clock = new FakeClock();
        var issued = Issuer(clock).Issue("play", "android");

        Assert.Equal(clock.GetUtcNow().AddHours(24), issued.ExpiresAt);
        var result = await Validator(clock).ValidateAsync(issued.Token);
        Assert.Equal(issued.ExpiresAt, result.Session!.ExpiresAt);
    }

    [Fact]
    public void Every_session_has_its_own_id()
    {
        var clock = new FakeClock();
        var issuer = Issuer(clock);

        Assert.NotEqual(issuer.Issue("play", "android").SessionId, issuer.Issue("play", "android").SessionId);
    }

    [Fact]
    public async Task An_expired_session_is_refused()
    {
        var clock = new FakeClock();
        var issued = Issuer(clock).Issue("play", "android");

        clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(2));

        Assert.Null((await Validator(clock).ValidateAsync(issued.Token)).Session);
    }

    [Fact]
    public async Task A_session_is_still_good_just_before_it_ends()
    {
        var clock = new FakeClock();
        var issued = Issuer(clock).Issue("play", "android");

        clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));

        Assert.NotNull((await Validator(clock).ValidateAsync(issued.Token)).Session);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var clock = new FakeClock();
        var forged = Issuer(clock, key: "a-completely-different-key-of-sufficient-length-0123456789").Issue("play", "android");

        Assert.Null((await Validator(clock).ValidateAsync(forged.Token)).Session);
    }

    [Fact]
    public async Task A_user_access_token_is_never_a_session()
    {
        // Even signed with the very same key: its audience and claims are a user's, so it is not a session.
        var userToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", "user-1"), new Claim("role", "Admin")]),
            Issuer = SessionIssuer,
            Audience = AppAttestationTestHost.UserAudience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AppAttestationTestHost.SessionKey)), SecurityAlgorithms.HmacSha256),
        });

        Assert.Null((await Validator(new FakeClock(DateTimeOffset.UtcNow)).ValidateAsync(userToken)).Session);
    }

    [Fact]
    public async Task A_session_signed_with_the_user_key_is_still_not_a_session()
    {
        // The reverse: a session signed with the user key is still refused, because the audience is the session's and the claims are missing.
        var clock = new FakeClock();
        var asUser = Issuer(clock, key: AppAttestationTestHost.UserKey).Issue("play", "android");

        Assert.Null((await Validator(clock).ValidateAsync(asUser.Token)).Session);
    }

    [Fact]
    public async Task A_session_for_another_issuer_or_audience_is_refused()
    {
        var clock = new FakeClock();
        var issued = Issuer(clock).Issue("play", "android");

        var otherAudience = new AppSessionValidator(AppAttestationTestHost.SessionKey, SessionIssuer, "another-audience", true, clock);
        var otherIssuer = new AppSessionValidator(AppAttestationTestHost.SessionKey, "another-issuer", SessionAudience, true, clock);

        Assert.Null((await otherAudience.ValidateAsync(issued.Token)).Session);
        Assert.Null((await otherIssuer.ValidateAsync(issued.Token)).Session);
    }

    [Fact]
    public async Task An_unattested_session_is_refused_outside_development()
    {
        var clock = new FakeClock();
        var issued = Issuer(clock).Issue("none", "windows");

        Assert.NotNull((await Validator(clock, acceptUnattested: true).ValidateAsync(issued.Token)).Session);
        Assert.Null((await Validator(clock, acceptUnattested: false).ValidateAsync(issued.Token)).Session);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not.a.jwt")]
    [InlineData("garbage")]
    public async Task A_missing_or_malformed_token_is_refused(string token)
    {
        Assert.Null((await Validator(new FakeClock()).ValidateAsync(token)).Session);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CHANGE-ME")]
    [InlineData("too-short")]
    public void A_missing_placeholder_or_short_key_stops_startup(string? key)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppSession:Key"] = key })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => services.AddAppAttestation(configuration, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment(), ValidSetup));

        Assert.Contains("AppSession:Key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_usable_key_passes_the_startup_check()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppSession:Key"] = AppAttestationTestHost.SessionKey })
            .Build();

        services.AddAppAttestation(configuration, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment(), ValidSetup);

        Assert.NotNull(services.BuildServiceProvider().GetRequiredService<AppSessionValidator>());
    }

    private static void ValidSetup(AppAttestationSetup setup)
    {
        setup.PackageName = AppAttestationTestHost.Package;
        setup.Issuer = SessionIssuer;
        setup.Audience = SessionAudience;
        setup.HeaderName = AppAttestationTestHost.Header;
        setup.CookieName = AppAttestationTestHost.Cookie;
        setup.RedirectPrefix = AppAttestationTestHost.Prefix;
        setup.SessionRequiredDetail = "detail";
        setup.SessionRequiredCode = AppAttestationCodes.SessionRequired;
        setup.AttestationFailedCode = AppAttestationCodes.AttestationFailed;
        setup.AttestationUnavailableCode = AppAttestationCodes.AttestationUnavailable;
    }
}
