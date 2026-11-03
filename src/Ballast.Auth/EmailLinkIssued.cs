// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Email;

namespace Ballast.Auth;

/// <summary>
/// The outcome of issuing a sign-in link. The link's secret is not in it: the secret was given only to the e-mail, and nothing the app keeps
/// (a log, a response, a return value) needs to hold it.
/// </summary>
/// <param name="Result">What the mail sender reported. The link row is kept whatever this is: a link nobody received cannot be used by anyone
/// else, because its secret is never stored.</param>
/// <param name="ExpiresAtUtc">When the link stops working, in UTC.</param>
public sealed record EmailLinkIssued(EmailSendResult Result, DateTime ExpiresAtUtc);
