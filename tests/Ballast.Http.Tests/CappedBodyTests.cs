// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Ballast.Http.Tests;

/// <summary>
/// The capped reads and the text decoding, ported from GetThere's <c>HttpBody</c> tests (<c>ScrapeTextDecodingTests</c>), with the
/// cap rules added: a declared length over the cap is refused before any byte is read, and a body read past the cap throws.
/// </summary>
public class CappedBodyTests
{
    [Fact]
    public void A_declared_windows_1250_body_decodes_to_its_letters()
    {
        // "café š" in windows-1250: é is 0xE9, š is 0x9A.
        byte[] bytes = [0x63, 0x61, 0x66, 0xE9, 0x20, 0x9A];

        var text = CappedBody.DecodeText(bytes, "windows-1250", null, "label");

        Assert.Equal("café š", text);
    }

    [Fact]
    public void An_unknown_declared_charset_is_warned_and_read_as_utf_8()
    {
        List<string> warnings = [];

        var text = CappedBody.DecodeText(Encoding.UTF8.GetBytes("abc"), "x-no-such-charset", warnings, "data.json");

        Assert.Equal("abc", text);
        Assert.Single(warnings, w => w.Contains("x-no-such-charset", StringComparison.Ordinal)
            && w.Contains("not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void A_utf_8_byte_order_mark_is_stripped_before_decoding()
    {
        var text = CappedBody.DecodeText(Encoding.UTF8.GetBytes("﻿{}"), null, [], "data.json");

        Assert.Equal("{}", text);
    }

    [Fact]
    public void Invalid_utf_8_without_a_declared_charset_is_warned_once_with_the_bytes_replaced_counted()
    {
        List<string> warnings = [];

        var text = CappedBody.DecodeText([0x7B, 0x8A, 0x7D], null, warnings, "data.json");

        Assert.Single(warnings);
        Assert.Contains("data.json is not valid UTF-8 (1 bytes replaced)", warnings[0], StringComparison.Ordinal);
        Assert.Contains('�', text);
    }

    [Fact]
    public void A_utf_16_byte_order_mark_decides_the_encoding()
    {
        var bytes = new byte[] { 0xFF, 0xFE, 0x61, 0x00, 0x62, 0x00 };

        Assert.Equal("ab", CappedBody.DecodeText(bytes, "utf-8", [], "label"));
    }

    [Fact]
    public void A_declared_charset_in_quotes_is_read_without_the_quotes()
    {
        byte[] bytes = [0x63, 0x61, 0x66, 0xE9];

        Assert.Equal("café", CappedBody.DecodeText(bytes, "\"windows-1250\"", null, "label"));
    }

    [Fact]
    public async Task A_response_with_an_unknown_charset_reports_the_fallback_through_the_callback()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("abc"))
        };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain; charset=x-no-such-charset");
        List<string> warnings = [];

        var text = await CappedBody.ReadStringAsync(response, 1024, warnings.Add);

        Assert.Equal("abc", text);
        Assert.Single(warnings, w => w.Contains("x-no-such-charset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_body_under_the_cap_is_read_whole()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };

        Assert.Equal(new byte[] { 1, 2, 3 }, await CappedBody.ReadBytesAsync(response, 3));
    }

    [Fact]
    public async Task A_negative_cap_is_an_argument_error_and_no_body_is_read()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CappedBody.ReadBytesAsync(response, -1));
    }

    [Fact]
    public async Task A_body_exactly_at_the_cap_is_kept()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[16]) };

        Assert.Equal(16, (await CappedBody.ReadBytesAsync(response, 16)).Length);
    }

    [Fact]
    public async Task A_body_past_the_cap_with_no_declared_length_is_refused_once_read_past_the_cap()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(new byte[20]))
        };

        var ex = await Assert.ThrowsAsync<BodyTooLargeException>(() => CappedBody.ReadBytesAsync(response, 10));

        Assert.Equal(10, ex.MaxBytes);
    }

    [Fact]
    public async Task A_declared_length_over_the_cap_is_refused_before_any_byte_is_read()
    {
        // The stream throws if it is read at all, so a pass proves no byte was read.
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ThrowingStream()) };
        response.Content.Headers.ContentLength = 100;

        await Assert.ThrowsAsync<BodyTooLargeException>(() => CappedBody.ReadBytesAsync(response, 10));
    }

    [Fact]
    public async Task The_string_read_refuses_a_body_past_the_cap_too()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[64]) };

        await Assert.ThrowsAsync<BodyTooLargeException>(() => CappedBody.ReadStringAsync(response, 8));
    }

    [Fact]
    public async Task A_cancelled_read_is_thrown_and_not_turned_into_a_cap()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CappedBody.ReadBytesAsync(response, 10, cancellation.Token));
    }

    [Fact]
    public async Task A_utf_8_json_upload_with_a_byte_order_mark_parses_as_text_without_it()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("﻿{\"a\":1}"))
        };

        Assert.Equal("{\"a\":1}", await CappedBody.ReadStringAsync(response, 1024));
    }

    /// <summary>A stream whose read fails the test. Used to prove the cap is checked before a read.</summary>
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body was read.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The body was read.");
    }
}
