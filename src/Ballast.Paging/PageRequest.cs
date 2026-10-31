// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel.DataAnnotations;

namespace Ballast.Paging;

/// <summary>
/// A page request: the 1-based <see cref="Page"/> and the number of rows on a page, <see cref="PerPage"/>. A query binds its
/// <c>page</c> and <c>perPage</c> parameters to these properties by name. A value outside its limits is refused by
/// <see cref="Validate"/>, which is run by <c>Validator.TryValidateObject</c>. It is never cut down silently.
/// </summary>
public record PageRequest : IValidatableObject
{
    /// <summary>The largest page a request may ask for.</summary>
    public const int MaxPage = 1_000_000;

    /// <summary>The 1-based page number. The default is 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>The number of rows on a page. The default is 50, and the largest allowed is <see cref="MaxPerPage"/>.</summary>
    public int PerPage { get; init; } = 50;

    /// <summary>
    /// The largest <see cref="PerPage"/> a request may ask for. It is 500. A project that wants a lower cap derives a record
    /// from <see cref="PageRequest"/> and overrides this property.
    /// </summary>
    protected virtual int MaxPerPage => 500;

    /// <summary>
    /// The number of rows to skip before this page. It is <see cref="Paging.SkipFor"/> of <see cref="Page"/> and
    /// <see cref="PerPage"/>, so it refuses what <see cref="Paging.SkipFor"/> refuses. Validate the request before reading it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="Page"/> or <see cref="PerPage"/> is below 1.</exception>
    public int Skip => Paging.SkipFor(Page, PerPage);

    /// <summary>
    /// Checks the request. <see cref="Page"/> must be from 1 to <see cref="MaxPage"/>, and <see cref="PerPage"/> must be from
    /// 1 to the largest page size. Each value outside those limits gives one result that names its member: <c>Page</c> or
    /// <c>PerPage</c>.
    /// </summary>
    /// <param name="validationContext">The validation context. It is not used.</param>
    /// <returns>One result for each value out of range. It is empty when the request is valid.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Page < 1 || Page > MaxPage)
        {
            yield return new ValidationResult($"page must be from 1 to {MaxPage}.", [nameof(Page)]);
        }

        if (PerPage < 1 || PerPage > MaxPerPage)
        {
            yield return new ValidationResult($"perPage must be from 1 to {MaxPerPage}.", [nameof(PerPage)]);
        }
    }
}
