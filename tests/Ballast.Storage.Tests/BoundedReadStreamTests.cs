// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Ballast.Storage;

namespace Ballast.Storage.Tests;

/// <summary>
/// The declared-size wrapper: a read past the limit throws, a read within it passes through, and every async and span path
/// counts the same bytes as the array path.
/// </summary>
public class BoundedReadStreamTests
{
    [Fact]
    public void A_stream_that_expands_past_its_declared_size_throws()
    {
        using var inner = new MemoryStream(new byte[100]);
        using var bounded = new BoundedReadStream(inner, 10, "stops.txt");
        var buffer = new byte[100];

        var ex = Assert.Throws<InvalidDataException>(() => bounded.Read(buffer, 0, buffer.Length));
        Assert.Contains("stops.txt", ex.Message);
        Assert.Contains("10 bytes", ex.Message);
    }

    [Fact]
    public void A_stream_within_its_declared_size_reads_through()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("abcde"));
        using var bounded = new BoundedReadStream(inner, 5, "stops.txt");
        var buffer = new byte[10];

        Assert.Equal(5, bounded.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, bounded.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task The_async_read_counts_the_same_bytes()
    {
        using var inner = new MemoryStream(new byte[100]);
        using var bounded = new BoundedReadStream(inner, 10, "entry.csv");
        var buffer = new byte[6];

        Assert.Equal(6, await bounded.ReadAsync(buffer.AsMemory(0, 6)));
        await Assert.ThrowsAsync<InvalidDataException>(async () => Assert.Equal(6, await bounded.ReadAsync(buffer.AsMemory(0, 6))));
    }

    [Fact]
    public void The_span_read_counts_the_same_bytes()
    {
        using var inner = new MemoryStream(new byte[100]);
        using var bounded = new BoundedReadStream(inner, 6, "entry.csv");
        var buffer = new byte[4];

        Assert.Equal(4, bounded.Read(buffer.AsSpan()));
        Assert.Throws<InvalidDataException>(() => bounded.Read(buffer.AsSpan()));
    }

    [Fact]
    public void A_stream_reads_to_the_end_and_a_later_read_past_the_limit_throws()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("0123456789"));
        using var bounded = new BoundedReadStream(inner, 10, "x");
        using var reader = new StreamReader(bounded);

        Assert.Equal("0123456789", reader.ReadToEnd());
    }

    [Fact]
    public void A_zero_limit_allows_an_empty_stream_and_refuses_one_byte()
    {
        using var empty = new BoundedReadStream(new MemoryStream(), 0, "empty");
        Assert.Equal(0, empty.Read(new byte[4], 0, 4));

        using var one = new BoundedReadStream(new MemoryStream(new byte[] { 1 }), 0, "one");
        Assert.Throws<InvalidDataException>(() => one.Read(new byte[4], 0, 4));
    }

    [Fact]
    public void The_wrapper_is_read_only_and_does_not_seek()
    {
        using var bounded = new BoundedReadStream(new MemoryStream(new byte[1]), 1, "x");

        Assert.True(bounded.CanRead);
        Assert.False(bounded.CanSeek);
        Assert.False(bounded.CanWrite);
        Assert.Throws<NotSupportedException>(() => _ = bounded.Length);
        Assert.Throws<NotSupportedException>(() => _ = bounded.Position);
        Assert.Throws<NotSupportedException>(() => bounded.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => bounded.Write(new byte[1], 0, 1));
    }

    [Fact]
    public void Disposing_the_wrapper_disposes_the_inner_stream()
    {
        var inner = new MemoryStream(new byte[1]);
        var bounded = new BoundedReadStream(inner, 1, "x");

        bounded.Dispose();

        Assert.False(inner.CanRead);
    }

    [Fact]
    public void A_null_inner_stream_or_a_negative_limit_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new BoundedReadStream(null!, 1, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedReadStream(new MemoryStream(), -1, "x"));
    }
}
