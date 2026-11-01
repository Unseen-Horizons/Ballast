// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>
/// A request to claim an existing record for a new send: a compare-and-set. The store changes the record only while it still
/// holds the state and the time that the ledger read, so two senders cannot both claim it.
/// </summary>
/// <param name="SubjectKey">The subject of the record.</param>
/// <param name="Kind">The kind of the record.</param>
/// <param name="ExpectedState">The state the record must still hold: Failed (a retry) or Pending (a stale send).</param>
/// <param name="ExpectedCreatedAtUtc">The record's time as the ledger read it. The claim applies only while it is unchanged.</param>
/// <param name="CreatedAtUtc">The time the record takes when the claim succeeds (see <see cref="ISendOnceStore.TryClaimAsync"/>).</param>
/// <param name="Recipient">The trimmed address the record is claimed for.</param>
public sealed record SendOnceClaim(string SubjectKey, string Kind, SendOnceState ExpectedState, DateTimeOffset ExpectedCreatedAtUtc, DateTimeOffset CreatedAtUtc, string Recipient);
