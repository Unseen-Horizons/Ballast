// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Ballast.Paging;

/// <summary>
/// One page of a list: its rows, the total number of rows, and where the page sits. Serialized with
/// <c>JsonSerializerDefaults.Web</c>, its JSON members are <c>data</c>, <c>total</c>, <c>page</c>, <c>perPage</c>,
/// <c>totalPages</c>, <c>hasNextPage</c> and <c>hasPreviousPage</c>. Installed apps read these names, so they do not change.
/// </summary>
public record PagedResult<T>
{
    /// <summary>
    /// Creates a result from its values as they were sent or read back. This is the constructor that System.Text.Json uses
    /// to read a result, so a server builds its result with the constructor that takes a <see cref="PageRequest"/> instead.
    /// </summary>
    /// <param name="data">The rows on the page. A copy is kept.</param>
    /// <param name="total">The number of rows in the whole list. It is not negative.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="perPage">The number of rows on a full page.</param>
    /// <param name="totalPages">The number of pages. It is stored as given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="total"/> is negative.</exception>
    [JsonConstructor]
    public PagedResult(IReadOnlyList<T> data, int total, int page, int perPage, int totalPages)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        Data = data.ToArray();
        Total = total;
        Page = page;
        PerPage = perPage;
        TotalPages = totalPages;
    }

    /// <summary>
    /// Creates the result for one page of a list. The page and the page size come from the request, and the number of pages
    /// is the total divided by the page size, rounded up. The constructor refuses only the values that cannot be used: a page
    /// below 1 and a page size below 1. It does not check the upper limits, which <see cref="PageRequest.Validate"/> checks.
    /// </summary>
    /// <param name="data">The rows on the page. A copy is kept.</param>
    /// <param name="total">The number of rows in the whole list. It is not negative.</param>
    /// <param name="request">The request the page answers. Its <see cref="PageRequest.Page"/> and <see cref="PageRequest.PerPage"/> must be 1 or more.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> or <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="total"/> is negative (the <c>ParamName</c> is <c>total</c>). Or <see cref="PageRequest.Page"/> or
    /// <see cref="PageRequest.PerPage"/> of the request is below 1 (the <c>ParamName</c> is <c>request</c>, and the message names the member).
    /// </exception>
    public PagedResult(IReadOnlyList<T> data, int total, PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        if (request.Page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Page, "request.Page must be 1 or more.");
        }

        if (request.PerPage < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.PerPage, "request.PerPage must be 1 or more.");
        }

        Data = data.ToArray();
        Total = total;
        Page = request.Page;
        PerPage = request.PerPage;
        TotalPages = (int)(((long)total + request.PerPage - 1) / request.PerPage);
    }

    /// <summary>The rows on this page. It is empty for a page past the last one.</summary>
    public IReadOnlyList<T> Data { get; }

    /// <summary>The number of rows in the whole list.</summary>
    public int Total { get; }

    /// <summary>The 1-based page number.</summary>
    public int Page { get; }

    /// <summary>The number of rows on a full page.</summary>
    public int PerPage { get; }

    /// <summary>The number of pages. It is 0 when the list is empty.</summary>
    public int TotalPages { get; }

    /// <summary>True when a page follows this one.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>True when this page is after the first.</summary>
    public bool HasPreviousPage => Page > 1;
}
