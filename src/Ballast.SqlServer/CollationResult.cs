// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.SqlServer;

/// <summary>What <see cref="CollationCheck"/> found about a database's text comparison.</summary>
public enum CollationResult
{
    /// <summary>The context's provider has no collations (for example an in-memory test provider). Nothing was checked.</summary>
    NotRelational,

    /// <summary>The database compares text without regard to case: the probe answered 1.</summary>
    CaseInsensitive,

    /// <summary>
    /// The database compares text with regard to case, or the probe gave an answer other than 1 (including no row). An unexpected
    /// answer counts as this: it is not evidence of a case-insensitive database.
    /// </summary>
    CaseSensitive,

    /// <summary>
    /// The probe could not run, for example because the database is unreachable. This is a connectivity fault, not a finding about
    /// the collation, so a startup check should log it and carry on.
    /// </summary>
    Unverified,
}
