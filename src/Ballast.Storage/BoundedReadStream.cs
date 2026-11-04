// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>
/// A read-only wrapper that throws once more than the limit of bytes has been read from the inner stream. It holds an archive
/// entry to the size it declared: a declared-size ceiling for the whole archive is only a sum of declared sizes, so without
/// this an entry that lies about its size could still expand past the ceiling while it is parsed or hashed.
/// <para>
/// The refusal is an <see cref="InvalidDataException"/> whose message names <c>what</c> and the declared size. Nothing is
/// silently cut short: a read that would go past the limit throws, and the bytes up to that point are already returned. The
/// wrapper does not seek, and it disposes the inner stream when it is disposed.
/// </para>
/// </summary>
public sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private readonly string _what;
    private long _read;

    /// <summary>Wraps <paramref name="inner"/> so that no more than <paramref name="limit"/> bytes can be read from it.</summary>
    /// <param name="inner">The stream to read. Required; it is disposed with this wrapper.</param>
    /// <param name="limit">The most bytes that may be read, the declared size. Zero or more.</param>
    /// <param name="what">What the stream is, for the error message, such as a file name. Used as written.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is negative.</exception>
    public BoundedReadStream(Stream inner, long limit, string what)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        _inner = inner;
        _limit = limit;
        _what = what ?? string.Empty;
    }

    private int Count(int n)
    {
        _read += n;
        if (_read > _limit)
        {
            throw new InvalidDataException($"{_what} expands past its declared size of {_limit} bytes");
        }

        return n;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => Count(_inner.Read(buffer));

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await _inner.ReadAsync(buffer, cancellationToken));

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the length is not known.</exception>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the stream does not seek.</exception>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the stream does not seek.</exception>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the stream is read-only.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the stream is read-only.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
