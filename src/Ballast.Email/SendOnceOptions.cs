// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>
/// The rules of <see cref="SendOnceLedger"/>. The defaults retry a Failed send for 48 hours after its first reservation, and never
/// reclaim a Pending record, because a crash in the middle of a send then means a mail that is not sent again rather than one
/// that is sent twice.
/// </summary>
public sealed class SendOnceOptions
{
    // A retried Failed record keeps its first time, so a send that is in flight can look older than PendingAfter. This margin
    // is longer than any send takes (the client's timeout is 30 seconds by default), so a send that is still running is never
    // reclaimed as stale.
    private static readonly TimeSpan _pendingMargin = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether a Failed record may be claimed again and sent again. Default true. With false, a Failed record is final, as is a
    /// Pending one (unless <see cref="PendingAfter"/> is set).
    /// </summary>
    public bool RetryFailed { get; set; } = true;

    /// <summary>
    /// How long after its first reservation a Failed record may still be retried. Once it is older than this, the failure is final,
    /// so a permanently failing address is not sent to for ever. Default 48 hours. Must be positive. Ignored when
    /// <see cref="RetryFailed"/> is false.
    /// </summary>
    public TimeSpan FailedRetryWindow { get; set; } = TimeSpan.FromHours(48);

    /// <summary>
    /// The age after which a Pending record may be claimed again, on the presumption that its send died with its process. Null, the
    /// default, means a Pending record is never reclaimed, so it blocks its e-mail for ever, as a crash mid-send always does.
    /// When set, it must be positive, and with <see cref="RetryFailed"/> on it must be at least
    /// <see cref="FailedRetryWindow"/> plus one hour (see <see cref="Validate"/>).
    /// </summary>
    public TimeSpan? PendingAfter { get; set; }

    /// <summary>
    /// Checks the options. The ledger calls this when it is created, so a misconfiguration stops the app at startup.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="FailedRetryWindow"/> is not positive, or <see cref="PendingAfter"/> is set and not positive, or, with
    /// <see cref="RetryFailed"/> on, <see cref="PendingAfter"/> is shorter than <see cref="FailedRetryWindow"/> plus one hour.
    /// </exception>
    public void Validate()
    {
        if (FailedRetryWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(FailedRetryWindow), FailedRetryWindow, "FailedRetryWindow must be a positive time span.");
        }

        if (PendingAfter is not { } pendingAfter)
        {
            return;
        }

        if (pendingAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PendingAfter), pendingAfter, "PendingAfter must be a positive time span, or null to never reclaim a Pending record.");
        }

        if (RetryFailed && pendingAfter < FailedRetryWindow + _pendingMargin)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PendingAfter),
                pendingAfter,
                "With RetryFailed on, PendingAfter must be at least FailedRetryWindow plus one hour. A retried record keeps its first time, so a shorter PendingAfter could reclaim a send that is still running.");
        }
    }
}
