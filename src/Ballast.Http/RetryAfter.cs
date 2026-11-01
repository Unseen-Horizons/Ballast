// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>
/// Reads a response's Retry-After header, in either form RFC 9110 allows: delta seconds, or an HTTP date.
/// </summary>
public static class RetryAfter
{
    /// <summary>Reads the header against the system clock. See <see cref="Read(HttpResponseMessage, TimeSpan, TimeProvider)"/>.</summary>
    /// <param name="response">The response to read. Required.</param>
    /// <param name="max">The longest wait returned. Zero or more.</param>
    /// <returns>The wait, capped at <paramref name="max"/>; null when the response has no usable Retry-After.</returns>
    public static TimeSpan? Read(HttpResponseMessage response, TimeSpan max) => Read(response, max, TimeProvider.System);

    /// <summary>
    /// Reads the response's Retry-After header. A delta is the wait itself. A date is the wait from <paramref name="clock"/>'s
    /// current instant to that date. A date that has passed gives zero: the request may be sent again now. A wait longer than
    /// <paramref name="max"/> is cut to it, so one busy answer cannot hold a caller for longer than the caller allows.
    /// </summary>
    /// <param name="response">The response to read. Required.</param>
    /// <param name="max">The longest wait returned. Zero or more.</param>
    /// <param name="clock">The instant the date is measured from. Required.</param>
    /// <returns>
    /// The wait, between zero and <paramref name="max"/>. Null when there is no Retry-After header, so the caller uses its own wait.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is below zero.</exception>
    public static TimeSpan? Read(HttpResponseMessage response, TimeSpan max, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(clock);
        if (max < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "The longest wait cannot be below zero.");
        }

        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        var wait = header.Delta ?? (header.Date is { } date ? date - clock.GetUtcNow() : null);
        if (wait is null)
        {
            return null;
        }

        if (wait.Value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }
        return wait.Value > max ? max : wait.Value;
    }
}
