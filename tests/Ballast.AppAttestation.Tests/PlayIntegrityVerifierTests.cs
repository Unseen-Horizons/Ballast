// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AppAttestation.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation.Tests;

/// <summary>
/// The Play Integrity decision, on decoded-verdict fixtures. Nothing here calls Google: the decoder is a fake behind
/// <see cref="IPlayIntegrityDecoder"/>.
/// </summary>
public class PlayIntegrityVerifierTests
{
    // 32 bytes, in the two spellings an owner meets: keytool's hex with colons, and Play's base64url.
    private const string DevHex = "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99";
    private static readonly string DevBase64Url = AppAttestationOptions.NormalizeDigest(DevHex)!;
    private const string OtherDigest = "Zm9vYmFyYmF6cXV4Zm9vYmFyYmF6cXV4Zm9vYmFyYmE";
    private const string Nonce = "bm9uY2Utbm9uY2Utbm9uY2U";

    private static AppAttestationSetup Setup() => new() { PackageName = AppAttestationTestHost.Package };

    private static PlayIntegrityVerifier Verifier(FakeDecoder decoder, params string[] developerCertificates) =>
        new(decoder, Options.Create(new AppAttestationOptions { DeveloperCertificates = developerCertificates }), Setup(), NullLogger<PlayIntegrityVerifier>.Instance);

    [Fact]
    public async Task A_play_recognised_app_on_a_genuine_device_is_accepted_as_play()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.PlayRecognised(Nonce) };

        var decision = await Verifier(decoder).VerifyAsync("t", Nonce);

        Assert.True(decision.Accepted);
        Assert.Equal("play", decision.Attestation);
    }

    [Fact]
    public async Task An_unrecognised_build_signed_with_a_listed_developer_certificate_is_the_developer_path()
    {
        // The owner lists the digest the way keytool prints it; Play returns it as base64url.
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(Nonce, DevBase64Url) };

        var decision = await Verifier(decoder, DevHex).VerifyAsync("t", Nonce);

        Assert.True(decision.Accepted);
        Assert.Equal("dev", decision.Attestation);
    }

    [Fact]
    public async Task An_unrecognised_build_signed_with_an_unknown_certificate_is_refused()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(Nonce, OtherDigest) };

        var decision = await Verifier(decoder, DevHex).VerifyAsync("t", Nonce);

        Assert.False(decision.Accepted);
    }

    [Fact]
    public async Task An_unrecognised_build_with_no_developer_certificate_configured_is_refused()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(Nonce, DevBase64Url) };

        var decision = await Verifier(decoder).VerifyAsync("t", Nonce);

        Assert.False(decision.Accepted);
    }

    [Fact]
    public async Task Every_certificate_in_the_verdict_must_be_a_developer_certificate()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(Nonce, DevBase64Url, OtherDigest) };

        var decision = await Verifier(decoder, DevHex).VerifyAsync("t", Nonce);

        Assert.False(decision.Accepted);
    }

    [Fact]
    public async Task A_padded_verdict_digest_matches_a_hex_configured_certificate()
    {
        // Google says base64url and does not say whether it pads: 32 bytes padded is 44 characters.
        var padded = DevBase64Url + "=";
        Assert.Equal(44, padded.Length);
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(Nonce, padded) };

        var decision = await Verifier(decoder, DevHex).VerifyAsync("t", Nonce);

        Assert.True(decision.Accepted);
        Assert.Equal("dev", decision.Attestation);
    }

    [Fact]
    public async Task A_standard_base64_verdict_digest_matches_a_hex_configured_certificate()
    {
        var standard = Convert.ToBase64String(Convert.FromHexString(DevHex.Replace(":", string.Empty, StringComparison.Ordinal)));
        Assert.Contains('/', standard);
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(Nonce, standard) };

        var decision = await Verifier(decoder, DevHex).VerifyAsync("t", Nonce);

        Assert.True(decision.Accepted);
        Assert.Equal("dev", decision.Attestation);
    }

    [Fact]
    public async Task A_verdict_with_one_listed_digest_and_one_malformed_digest_is_refused()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(Nonce, DevBase64Url, "not-a-digest!") };

        var decision = await Verifier(decoder, DevHex).VerifyAsync("t", Nonce);

        Assert.False(decision.Accepted);
        Assert.Null(decision.Attestation);
    }

    [Fact]
    public async Task A_device_that_fails_device_integrity_is_refused_even_when_the_app_is_recognised()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.PlayRecognised(Nonce) with { DeviceRecognitionVerdicts = ["MEETS_BASIC_INTEGRITY"] } };

        Assert.False((await Verifier(decoder).VerifyAsync("t", Nonce)).Accepted);
    }

    [Fact]
    public async Task A_device_that_fails_device_integrity_is_refused_on_the_developer_path_too()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(Nonce, DevBase64Url) with { DeviceRecognitionVerdicts = [] } };

        Assert.False((await Verifier(decoder, DevHex).VerifyAsync("t", Nonce)).Accepted);
    }

    [Fact]
    public async Task An_unevaluated_app_is_refused()
    {
        var decoder = new FakeDecoder
        {
            Verdict = new IntegrityVerdict(AppAttestationTestHost.Package, Nonce, "UNEVALUATED", null, [], ["MEETS_DEVICE_INTEGRITY"])
        };

        Assert.False((await Verifier(decoder, DevHex).VerifyAsync("t", Nonce)).Accepted);
    }

    [Theory]
    [InlineData("com.someone.else", AppAttestationTestHost.Package)]
    [InlineData(AppAttestationTestHost.Package, "com.someone.else")]
    public async Task A_verdict_naming_another_package_is_refused(string requestPackage, string appPackage)
    {
        var decoder = new FakeDecoder
        {
            Verdict = new IntegrityVerdict(requestPackage, Nonce, "PLAY_RECOGNIZED", appPackage, [OtherDigest], ["MEETS_DEVICE_INTEGRITY"])
        };

        Assert.False((await Verifier(decoder).VerifyAsync("t", Nonce)).Accepted);
    }

    [Fact]
    public async Task A_verdict_for_the_package_the_setup_names_is_the_only_one_accepted()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.PlayRecognised(Nonce) };
        var verifier = new PlayIntegrityVerifier(
            decoder,
            Options.Create(new AppAttestationOptions()),
            new AppAttestationSetup { PackageName = "com.sample.other" },
            NullLogger<PlayIntegrityVerifier>.Instance);

        Assert.False((await verifier.VerifyAsync("t", Nonce)).Accepted);
    }

    [Fact]
    public async Task A_padded_echo_of_the_same_nonce_is_accepted()
    {
        // The server issues 16 random bytes as unpadded base64url (22 chars); Google may echo the padded form.
        var issued = System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var padded = issued + "==";
        var decoder = new FakeDecoder { Verdict = Verdicts.Sideloaded(padded, DevBase64Url) };

        var decision = await Verifier(decoder, DevHex).VerifyAsync("t", issued);

        Assert.True(decision.Accepted);
        Assert.Equal("dev", decision.Attestation);
    }

    [Fact]
    public async Task A_standard_base64_echo_of_the_same_nonce_is_accepted()
    {
        var bytes = new byte[16];
        Array.Fill(bytes, (byte)0xFB); // encodes with '+' / '/' in standard base64, '-' / '_' in base64url
        var issued = System.Buffers.Text.Base64Url.EncodeToString(bytes);
        var standard = Convert.ToBase64String(bytes);
        Assert.Contains('/', standard);
        var decoder = new FakeDecoder { Verdict = Verdicts.PlayRecognised(standard) };

        Assert.True((await Verifier(decoder).VerifyAsync("t", issued)).Accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    public void A_missing_or_undecodable_nonce_never_matches(string? fromVerdict)
    {
        Assert.False(PlayIntegrityVerifier.NoncesMatch(fromVerdict, Nonce));
        Assert.False(PlayIntegrityVerifier.NoncesMatch(Nonce, fromVerdict ?? string.Empty));
    }

    [Fact]
    public async Task A_token_that_answers_another_nonce_is_refused()
    {
        var decoder = new FakeDecoder { Verdict = Verdicts.PlayRecognised("a-different-nonce") };

        Assert.False((await Verifier(decoder).VerifyAsync("t", Nonce)).Accepted);
    }

    [Fact]
    public async Task A_failure_to_reach_google_is_not_a_decision_it_throws()
    {
        var decoder = new FakeDecoder { Failure = new AttestationUnavailableException("Google is down") };

        await Assert.ThrowsAsync<AttestationUnavailableException>(() => Verifier(decoder).VerifyAsync("t", Nonce));
    }

    [Fact]
    public async Task The_real_decoder_with_no_service_account_is_unavailable_and_calls_nothing()
    {
        using var decoder = new GooglePlayIntegrityDecoder(
            Options.Create(new PlayIntegrityOptions()), Setup(), NullLogger<GooglePlayIntegrityDecoder>.Instance);

        var error = await Assert.ThrowsAsync<AttestationUnavailableException>(() => decoder.DecodeAsync("t"));
        Assert.Contains("PlayIntegrity:ServiceAccountJson", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_real_decoder_names_the_service_account_setting_the_setup_gives()
    {
        using var decoder = new GooglePlayIntegrityDecoder(
            Options.Create(new PlayIntegrityOptions()),
            new AppAttestationSetup { PackageName = AppAttestationTestHost.Package, PlayIntegritySection = "SampleGoogle" },
            NullLogger<GooglePlayIntegrityDecoder>.Instance);

        var error = await Assert.ThrowsAsync<AttestationUnavailableException>(() => decoder.DecodeAsync("t"));
        Assert.Contains("SampleGoogle:ServiceAccountJson", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_real_decoder_refuses_a_service_account_that_is_not_a_credential_and_never_calls_google()
    {
        using var decoder = new GooglePlayIntegrityDecoder(
            Options.Create(new PlayIntegrityOptions { ServiceAccountJson = "{ not json" }), Setup(), NullLogger<GooglePlayIntegrityDecoder>.Instance);

        var error = await Assert.ThrowsAsync<AttestationUnavailableException>(() => decoder.DecodeAsync("t"));
        Assert.Contains("not usable", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99", true)]
    [InlineData("aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899", true)]
    [InlineData("Zm9vYmFyYmF6cXV4Zm9vYmFyYmF6cXV4Zm9vYmFyYmE", true)]
    [InlineData("Zm9vYmFyYmF6cXV4Zm9vYmFyYmF6cXV4Zm9vYmFyYmE=", true)]
    [InlineData("not a digest", false)]
    [InlineData("AA:BB", false)]
    [InlineData("", false)]
    public void A_developer_certificate_is_read_from_hex_or_base64url_and_anything_else_is_reported(string entry, bool valid)
    {
        var options = new AppAttestationOptions { DeveloperCertificates = [entry] };

        var digests = options.NormalizedDeveloperDigests(out var rejected);

        if (valid)
        {
            Assert.Single(digests);
            Assert.Empty(rejected);
        }
        else
        {
            Assert.Empty(digests);
            if (entry.Length > 0)
                Assert.Single(rejected);
        }
    }

    [Fact]
    public void Hex_and_base64url_spellings_of_one_digest_are_the_same_digest()
    {
        var options = new AppAttestationOptions { DeveloperCertificates = [DevHex + "," + DevBase64Url] };

        Assert.Single(options.NormalizedDeveloperDigests(out _));
    }
}
