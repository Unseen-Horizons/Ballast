// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Paging;

/// <summary>
/// The row offset of a paged list. <c>(page - 1) * perPage</c> in <c>int</c> wraps for a very large page, so the offset is
/// computed in <c>long</c> and capped at <see cref="int.MaxValue"/>.
/// </summary>
public static class Paging
{
    /// <summary>
    /// The number of rows to skip before a page. Pages are 1-based. The offset is computed in <c>long</c> and capped at
    /// <see cref="int.MaxValue"/>. An offset that large is past the end of any list a database can return, so the page comes
    /// back empty with its real total. That is a true answer, not a guess.
    /// </summary>
    /// <param name="page">The 1-based page number. Must be 1 or more.</param>
    /// <param name="perPage">The number of rows on a page. Must be 1 or more.</param>
    /// <returns>The offset of the first row on the page, from 0 to <see cref="int.MaxValue"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> or <paramref name="perPage"/> is below 1. The exception's <c>ParamName</c> is the parameter that is below 1.
    /// </exception>
    public static int SkipFor(int page, int perPage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        return (int)Math.Min((long)(page - 1) * perPage, int.MaxValue);
    }
}
