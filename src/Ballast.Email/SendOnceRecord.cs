// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>
/// One record in an app's store: the state of one kind of e-mail to one subject. The store hands these back from
/// <see cref="ISendOnceStore.FindAsync"/>, and the ledger hands one to <see cref="ISendOnceStore.TryReserveAsync"/> to insert it.
/// </summary>
/// <param name="SubjectKey">The subject the e-mail is about, as the app names it (for example its record's id).</param>
/// <param name="Kind">The kind of e-mail, as the app names it.</param>
/// <param name="State">The state of the record.</param>
/// <param name="Recipient">The trimmed address the e-mail went to or was reserved for, or null when the subject has none.</param>
/// <param name="CreatedAtUtc">
/// The time the record was reserved, in UTC, for a record that has been Failed. For a Pending record it is the time the
/// current attempt was claimed. See <see cref="ISendOnceStore"/> for why.
/// </param>
public sealed record SendOnceRecord(string SubjectKey, string Kind, SendOnceState State, string? Recipient, DateTimeOffset CreatedAtUtc);
