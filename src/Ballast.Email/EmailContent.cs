// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>The parts of one e-mail that <see cref="SendOnceLedger"/> sends: the subject, the plain-text body and an optional HTML body.</summary>
/// <param name="Subject">The subject line.</param>
/// <param name="Text">The plain-text body.</param>
/// <param name="Html">The HTML body, or null for a plain-text e-mail.</param>
public sealed record EmailContent(string Subject, string Text, string? Html = null);
