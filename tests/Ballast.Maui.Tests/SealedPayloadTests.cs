// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;

using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>The sealed-file format: the layout, a fresh nonce for every seal, and a file that is altered or sealed under another key refuses to open.</summary>
public sealed class SealedPayloadTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(SealedKeyProvider.KeyBytes);

    [Fact]
    public void Text_round_trips_under_the_same_key()
    {
        var key = NewKey();
        const string text = "Plain text, with ü and ✓";

        Assert.Equal(text, SealedPayload.Open(key, SealedPayload.Seal(key, text)));
    }

    [Fact]
    public void The_layout_is_nonce_then_tag_then_ciphertext()
    {
        var sealedBytes = SealedPayload.Seal(NewKey(), "abc");

        Assert.Equal(SealedPayload.NonceBytes + SealedPayload.TagBytes + 3, sealedBytes.Length);
    }

    [Fact]
    public void A_fresh_nonce_is_used_for_every_seal()
    {
        var key = NewKey();

        var first = SealedPayload.Seal(key, "same text");
        var second = SealedPayload.Seal(key, "same text");

        Assert.False(first.AsSpan(0, SealedPayload.NonceBytes).SequenceEqual(second.AsSpan(0, SealedPayload.NonceBytes)));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void An_empty_text_is_only_the_nonce_and_the_tag()
    {
        var key = NewKey();
        var sealedBytes = SealedPayload.Seal(key, string.Empty);

        Assert.Equal(SealedPayload.NonceBytes + SealedPayload.TagBytes, sealedBytes.Length);
        Assert.Equal(string.Empty, SealedPayload.Open(key, sealedBytes));
    }

    [Fact]
    public void Bytes_round_trip_with_their_exact_content()
    {
        var key = NewKey();
        var plain = RandomNumberGenerator.GetBytes(4096);

        Assert.Equal(plain, SealedPayload.OpenBytes(key, SealedPayload.SealBytes(key, plain)));
    }

    [Fact]
    public void Another_key_cannot_open_the_file()
    {
        var sealedBytes = SealedPayload.Seal(NewKey(), "secret");

        Assert.ThrowsAny<CryptographicException>(() => SealedPayload.Open(NewKey(), sealedBytes));
    }

    [Fact]
    public void A_tampered_byte_fails_to_open()
    {
        var key = NewKey();
        var sealedBytes = SealedPayload.Seal(key, "secret");
        sealedBytes[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => SealedPayload.Open(key, sealedBytes));
    }

    [Fact]
    public void A_file_too_short_to_hold_the_nonce_and_the_tag_is_refused()
    {
        var shortFile = new byte[SealedPayload.NonceBytes + SealedPayload.TagBytes - 1];

        var error = Assert.Throws<CryptographicException>(() => SealedPayload.Open(NewKey(), shortFile));

        Assert.Contains("too short", error.Message, StringComparison.Ordinal);
    }
}
