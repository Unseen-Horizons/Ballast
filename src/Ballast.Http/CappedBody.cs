// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.Text;

namespace Ballast.Http;

/// <summary>
/// Reads a response body under a size ceiling, and decodes text with the charset rules a document needs. Each caller keeps
/// its own error: a body past the cap throws <see cref="BodyTooLargeException"/> and nothing is returned.
/// </summary>
public static class CappedBody
{
    static CappedBody()
    {
        // The legacy code pages (windows-1250, ISO-8859-2 and the rest) ship in the shared framework but only
        // resolve once registered. Registration is process-wide and idempotent, and it only adds encodings.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Reads the body as bytes, aborting past <paramref name="maxBytes"/> so a lying or absent Content-Length cannot exhaust
    /// memory. A declared length over the cap is refused before a byte is read.
    /// </summary>
    /// <param name="response">The response whose body is read. Required.</param>
    /// <param name="maxBytes">The most bytes the body may hold.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The body's bytes, at most <paramref name="maxBytes"/> of them.</returns>
    /// <exception cref="BodyTooLargeException">The declared length, or the bytes read, are over the cap.</exception>
    public static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            throw new BodyTooLargeException(maxBytes);
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new BodyTooLargeException(maxBytes);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// <see cref="ReadBytesAsync"/> decoded with the charset the server declared. Reading bytes rather than letting the content
    /// decode is what makes the cap enforceable, so the decode happens here. An unknown charset label falls back to UTF-8, and
    /// <paramref name="onWarning"/> is told about it so the fallback is not silent. A byte order mark is stripped only when it is
    /// the one the chosen encoding writes.
    /// </summary>
    /// <param name="response">The response whose body is read. Required.</param>
    /// <param name="maxBytes">The most bytes the body may hold.</param>
    /// <param name="onWarning">Receives a warning when the declared charset is not supported. May be null.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The body as text.</returns>
    /// <exception cref="BodyTooLargeException">The declared length, or the bytes read, are over the cap.</exception>
    public static async Task<string> ReadStringAsync(
        HttpResponseMessage response, long maxBytes, Action<string>? onWarning = null, CancellationToken ct = default)
    {
        var bytes = await ReadBytesAsync(response, maxBytes, ct);

        var encoding = Encoding.UTF8;
        var charset = response.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = Encoding.GetEncoding(charset.Trim('"')); }
            catch (ArgumentException)
            {
                onWarning?.Invoke($"Declared charset {charset.Trim('"')} is not supported; read as UTF-8");
            }
        }

        var preamble = encoding.GetPreamble();
        var start = StartsWith(bytes, preamble) ? preamble.Length : 0;
        return encoding.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>
    /// Decodes a document's bytes into text for the readers that take a string. A UTF-8 or UTF-16 byte order mark decides the
    /// encoding and is stripped. Otherwise a declared charset is used (an unknown label is warned about, then the bytes are read
    /// as UTF-8). With neither, the bytes must be valid UTF-8: when they are not, a warning says how many bytes were replaced and
    /// the text is decoded with replacement characters. Nothing is silently re-decoded as another encoding.
    /// </summary>
    /// <param name="bytes">The document's bytes. Required.</param>
    /// <param name="declaredCharset">The charset the server or the file declared, or null.</param>
    /// <param name="warnings">Receives each warning. May be null, in which case no warning is kept.</param>
    /// <param name="label">What the document is called in a warning, such as its file name.</param>
    /// <returns>The decoded text.</returns>
    public static string DecodeText(byte[] bytes, string? declaredCharset, ICollection<string>? warnings, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(label);

        Encoding[] byteOrderMarked = [Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode];
        foreach (var candidate in byteOrderMarked)
        {
            var preamble = candidate.GetPreamble();
            if (StartsWith(bytes, preamble))
            {
                return candidate.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
            }
        }

        if (!string.IsNullOrWhiteSpace(declaredCharset))
        {
            try
            {
                return Encoding.GetEncoding(declaredCharset.Trim('"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
                warnings?.Add($"{label}: declared charset {declaredCharset.Trim('"')} is not supported; the bytes are read as UTF-8");
            }
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            var replaced = CountInvalidUtf8Bytes(bytes);
            warnings?.Add($"{label} is not valid UTF-8 ({replaced:N0} bytes replaced); names may be garbled - save the file as UTF-8");
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>The number of bytes that are not valid UTF-8: each invalid or truncated sequence counts its own length.</summary>
    private static int CountInvalidUtf8Bytes(byte[] bytes)
    {
        var invalid = 0;
        var rest = bytes.AsSpan();
        while (!rest.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(rest, out _, out var consumed);
            if (status != OperationStatus.Done)
            {
                invalid += consumed;
            }
            rest = rest[Math.Max(consumed, 1)..];
        }

        return invalid;
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix) =>
        bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}
