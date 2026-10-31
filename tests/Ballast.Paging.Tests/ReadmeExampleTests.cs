// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel.DataAnnotations;

using Ballast.Paging;

namespace Ballast.Paging.Tests;

/// <summary>
/// Runs the example in src/Ballast.Paging/README.md exactly as it is written there. The README's using lines are at the top of
/// this file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public void The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.Paging/README.md.
        // The request a query binds: ?page=2&perPage=25. Check it before the query runs.
        var request = new PageRequest { Page = 2, PerPage = 25 };
        List<ValidationResult> refusals = [];
        bool valid = Validator.TryValidateObject(request, new ValidationContext(request), refusals, validateAllProperties: true);

        // The rows to skip, and the answer for the page.
        var rows = new[] { "row 26", "row 27" };
        var page = new PagedResult<string>(rows, total: 60, request);
        Console.WriteLine($"valid={valid} skip={request.Skip} totalPages={page.TotalPages} next={page.HasNextPage}");

        Assert.True(valid);
        Assert.Equal(25, request.Skip);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNextPage);
    }
}
