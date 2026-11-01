// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>What one call to <see cref="SendOnceLedger.SendOnceAsync"/> did.</summary>
public enum SendOnceOutcome
{
    /// <summary>This call sent the e-mail, and it was delivered.</summary>
    Sent,

    /// <summary>This call tried to send the e-mail, and it was not delivered. The record is Failed.</summary>
    Failed,

    /// <summary>This call recorded that the subject has no address. Nothing was sent.</summary>
    NoAddress,

    /// <summary>A Sent or NoAddress record already exists, so there is nothing to do. Nothing was sent.</summary>
    AlreadyRecorded,

    /// <summary>
    /// A Failed record exists that is final: retries are off, or the record is older than the retry window, or there is no
    /// address to retry with. Nothing was sent.
    /// </summary>
    AlreadyFailed,

    /// <summary>
    /// A Pending record exists that is not stale, or another sender took the reservation or the claim first. Nothing was sent by
    /// this call.
    /// </summary>
    InProgress,
}
