// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Ballast.AppAttestation.Tests.Support;

using Microsoft.Extensions.DependencyInjection;

namespace Ballast.AppAttestation.Tests;

/// <summary>
/// <c>app/session</c> through the real controller and manager: the nonce, the attested session, and every way it must say no. The
/// decoder is a fake, so Google is never called.
/// </summary>
public class AppSessionBootstrapTests
{
    private const string DevHex = "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99";

    private static async Task<string> NonceAsync(AppAttestationTestHost host)
    {
        var response = await host.Client.GetAsync("/app/session/nonce");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("nonce").GetString()!;
    }

    private static Task<HttpResponseMessage> PostSessionAsync(AppAttestationTestHost host, string nonce, string? token = "opaque-token", string platform = "android", string installId = "install-0000000001") =>
        host.Client.PostAsJsonAsync("/app/session", new { platform, installId, nonce, integrityToken = token });

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    [Fact]
    public async Task A_play_recognised_install_gets_a_session_that_says_play()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false);
        var nonce = await NonceAsync(host);
        host.Decoder.Verdict = Verdicts.PlayRecognised(nonce);

        var response = await PostSessionAsync(host, nonce);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(24 * 3600, body.GetProperty("expiresInSeconds").GetInt32());

        var validator = host.Services.GetRequiredService<AppSessionValidator>();
        var session = (await validator.ValidateAsync(body.GetProperty("accessToken").GetString())).Session!;
        Assert.Equal("play", session.Attestation);
        Assert.Equal("android", session.Platform);
        Assert.Contains(host.Logs.Lines, l => l.Contains("App session issued") && l.Contains("att=play") && l.Contains("plat=android"));
    }

    [Fact]
    public async Task The_owners_sideloaded_build_gets_a_dev_session()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false, developerCertificates: [DevHex]);
        var nonce = await NonceAsync(host);
        host.Decoder.Verdict = Verdicts.Sideloaded(nonce, AppAttestationOptions.NormalizeDigest(DevHex)!);

        var response = await PostSessionAsync(host, nonce);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(host.Logs.Lines, l => l.Contains("App session issued") && l.Contains("att=dev"));
    }

    [Fact]
    public async Task An_unknown_sideloaded_build_is_refused_with_403_and_no_session()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false, developerCertificates: [DevHex]);
        var nonce = await NonceAsync(host);
        host.Decoder.Verdict = Verdicts.Sideloaded(nonce, "Zm9vYmFyYmF6cXV4Zm9vYmFyYmF6cXV4Zm9vYmFyYmE");

        var response = await PostSessionAsync(host, nonce);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("APP_ATTESTATION_FAILED", await CodeAsync(response));
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains("App session issued"));

        // The refusal logs the verdict's own fields, and never the token.
        Assert.Contains(host.Logs.Lines, l => l.Contains("appRecognitionVerdict=UNRECOGNIZED_VERSION"));
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains("opaque-token"));
    }

    [Fact]
    public async Task A_google_failure_is_503_and_never_a_session()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false);
        var nonce = await NonceAsync(host);
        host.Decoder.Failure = new AttestationUnavailableException("Google is down");

        var response = await PostSessionAsync(host, nonce);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("APP_ATTESTATION_UNAVAILABLE", await CodeAsync(response));
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains("App session issued"));
        Assert.Contains(host.Logs.Lines, l => l.StartsWith("Error", StringComparison.Ordinal) && l.Contains("attestation unavailable"));
    }

    [Fact]
    public async Task A_nonce_works_once()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false);
        var nonce = await NonceAsync(host);
        host.Decoder.Verdict = Verdicts.PlayRecognised(nonce);

        Assert.Equal(HttpStatusCode.OK, (await PostSessionAsync(host, nonce)).StatusCode);

        var again = await PostSessionAsync(host, nonce);
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Equal("APP_ATTESTATION_FAILED", await CodeAsync(again));
    }

    [Fact]
    public async Task A_nonce_that_was_never_issued_is_refused_before_google_is_asked()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false);
        host.Decoder.Verdict = Verdicts.PlayRecognised("made-up-nonce");

        var response = await PostSessionAsync(host, "made-up-nonce");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Decoder.Calls);
    }

    [Fact]
    public async Task A_nonce_expires_after_five_minutes()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false);
        var nonce = await NonceAsync(host);
        host.Decoder.Verdict = Verdicts.PlayRecognised(nonce);

        host.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.Forbidden, (await PostSessionAsync(host, nonce)).StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_nonce_is_refused_and_the_real_nonce_is_spent()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false);
        var nonce = await NonceAsync(host);
        host.Decoder.Verdict = Verdicts.PlayRecognised("a-different-nonce");

        Assert.Equal(HttpStatusCode.Forbidden, (await PostSessionAsync(host, nonce)).StatusCode);

        host.Decoder.Verdict = Verdicts.PlayRecognised(nonce);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostSessionAsync(host, nonce)).StatusCode);
    }

    [Fact]
    public async Task A_missing_integrity_token_is_refused_in_production_even_when_the_bypass_flag_is_set()
    {
        // AllowUnattested is a Development-only switch: elsewhere it is ignored.
        await using var host = await AppAttestationTestHost.StartAsync(environment: "Production", allowUnattested: true);
        var nonce = await NonceAsync(host);

        var response = await PostSessionAsync(host, nonce, token: null, platform: "windows");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("APP_ATTESTATION_FAILED", await CodeAsync(response));
        Assert.Equal(0, host.Decoder.Calls);
    }

    [Fact]
    public async Task In_development_the_bypass_issues_an_unattested_session_without_asking_google()
    {
        await using var host = await AppAttestationTestHost.StartAsync(environment: "Development", allowUnattested: true);
        var nonce = await NonceAsync(host);

        var response = await PostSessionAsync(host, nonce, token: null, platform: "windows");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, host.Decoder.Calls);
        Assert.Contains(host.Logs.Lines, l => l.Contains("App session issued") && l.Contains("att=none") && l.Contains("plat=windows"));
    }

    [Fact]
    public async Task In_development_without_the_flag_there_is_no_bypass()
    {
        await using var host = await AppAttestationTestHost.StartAsync(environment: "Development", allowUnattested: false);
        var nonce = await NonceAsync(host);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostSessionAsync(host, nonce, token: null)).StatusCode);
    }

    [Fact]
    public async Task Production_logs_an_error_at_startup_when_the_bypass_flag_is_set()
    {
        await using var host = await AppAttestationTestHost.StartAsync(environment: "Production", allowUnattested: true);

        // The start-up check is a hosted service, which the test host runs when it starts.
        Assert.Contains(host.Logs.Lines, l => l.StartsWith("Error", StringComparison.Ordinal) && l.Contains("AllowUnattested") && l.Contains("Production"));
    }

    [Fact]
    public async Task A_session_request_for_a_platform_the_setup_does_not_name_is_400()
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false, adjustSetup: setup => setup.Platforms = ["android"]);
        var nonce = await NonceAsync(host);

        var response = await PostSessionAsync(host, nonce, platform: "windows");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await CodeAsync(response));
    }

    [Theory]
    [InlineData("", "install-0000000001", "n")]
    [InlineData("plan9", "install-0000000001", "n")]
    [InlineData("android", "short", "n")]
    [InlineData("android", "install-0000000001", "")]
    public async Task A_malformed_request_is_400_before_anything_is_consumed(string platform, string installId, string nonce)
    {
        await using var host = await AppAttestationTestHost.StartAsync(allowUnattested: false);

        var response = await host.Client.PostAsJsonAsync("/app/session", new { platform, installId, nonce, integrityToken = "t" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_bootstrap_has_its_own_limit_of_ten_a_minute_per_address()
    {
        await using var host = await AppAttestationTestHost.StartAsync();

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/app/session/nonce")).StatusCode);

        var eleventh = await host.Client.GetAsync("/app/session/nonce");
        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
    }

    [Fact]
    public async Task A_full_nonce_store_answers_503_and_evicts_nothing()
    {
        await using var host = await AppAttestationTestHost.StartAsync(storeCapacity: 2);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/app/session/nonce")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/app/session/nonce")).StatusCode);

        var third = await host.Client.GetAsync("/app/session/nonce");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, third.StatusCode);
        Assert.Equal("APP_ATTESTATION_UNAVAILABLE", await CodeAsync(third));
    }

    [Fact]
    public async Task The_codes_a_refusal_answers_with_are_the_ones_the_setup_names()
    {
        await using var host = await AppAttestationTestHost.StartAsync(
            allowUnattested: false,
            adjustSetup: setup =>
            {
                setup.AttestationFailedCode = "SAMPLE_REFUSED";
                setup.AttestationUnavailableCode = "SAMPLE_UNAVAILABLE";
            });
        var nonce = await NonceAsync(host);
        host.Decoder.Failure = new AttestationUnavailableException("Google is down");

        var unavailable = await PostSessionAsync(host, nonce);
        Assert.Equal("SAMPLE_UNAVAILABLE", await CodeAsync(unavailable));

        var refused = await PostSessionAsync(host, "never-issued");
        Assert.Equal("SAMPLE_REFUSED", await CodeAsync(refused));
    }
}
