// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel.DataAnnotations;

using Ballast.Paging;

namespace Ballast.Paging.Tests;

public class PageRequestTests
{
    /// <summary>A request that allows at most 200 rows a page, as a project with a lower cap would derive.</summary>
    private sealed record SmallPage : PageRequest
    {
        protected override int MaxPerPage => 200;
    }

    /// <summary>A request whose cap is below its default page size, so its default is refused.</summary>
    private sealed record TinyPage : PageRequest
    {
        protected override int MaxPerPage => 20;
    }

    [Fact]
    public void The_defaults_are_page_1_and_50_rows()
    {
        var request = new PageRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(50, request.PerPage);
        Assert.Equal(0, request.Skip);
        Assert.True(TryValidate(request, out _));
    }

    [Fact]
    public void Skip_is_the_rows_before_the_page()
    {
        Assert.Equal(50, new PageRequest { Page = 3, PerPage = 25 }.Skip);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    public void Skip_refuses_a_page_or_page_size_below_1_as_skipfor_does(int page, int perPage)
    {
        var request = new PageRequest { Page = page, PerPage = perPage };

        Assert.Throws<ArgumentOutOfRangeException>(() => request.Skip);
    }

    [Fact]
    public void An_absurd_page_gives_a_valid_offset_and_is_refused_by_validation()
    {
        var request = new PageRequest { Page = int.MaxValue, PerPage = 500 };

        Assert.Equal(int.MaxValue, request.Skip);
        Assert.False(TryValidate(request, out var results));
        var result = Assert.Single(results);
        Assert.Equal("Page", Assert.Single(result.MemberNames));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public void A_page_from_1_to_the_largest_is_valid(int page) =>
        Assert.True(TryValidate(new PageRequest { Page = page }, out _));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void A_page_outside_1_to_the_largest_is_refused_and_names_page(int page)
    {
        Assert.False(TryValidate(new PageRequest { Page = page }, out var results));

        var result = Assert.Single(results);
        Assert.Equal("Page", Assert.Single(result.MemberNames));
        Assert.Contains("page", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void A_page_size_from_1_to_500_is_valid(int perPage) =>
        Assert.True(TryValidate(new PageRequest { PerPage = perPage }, out _));

    [Fact]
    public void A_page_size_of_501_is_a_validation_error_that_names_perPage()
    {
        Assert.False(TryValidate(new PageRequest { PerPage = 501 }, out var results));

        var result = Assert.Single(results);
        Assert.Equal("PerPage", Assert.Single(result.MemberNames));
        Assert.Contains("perPage", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_page_size_below_1_is_refused_and_names_perPage(int perPage)
    {
        Assert.False(TryValidate(new PageRequest { PerPage = perPage }, out var results));

        Assert.Equal("PerPage", Assert.Single(Assert.Single(results).MemberNames));
    }

    [Fact]
    public void A_bad_page_and_a_bad_page_size_give_one_result_each()
    {
        Assert.False(TryValidate(new PageRequest { Page = 0, PerPage = 501 }, out var results));

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.MemberNames.Contains("Page"));
        Assert.Contains(results, r => r.MemberNames.Contains("PerPage"));
    }

    [Fact]
    public void A_derived_request_with_a_cap_of_200_refuses_201()
    {
        Assert.True(TryValidate(new SmallPage { PerPage = 200 }, out _));
        Assert.False(TryValidate(new SmallPage { PerPage = 201 }, out var results));

        Assert.Equal("PerPage", Assert.Single(Assert.Single(results).MemberNames));
    }

    [Fact]
    public void A_derived_request_refuses_a_page_size_above_the_base_cap_instead_of_cutting_it()
    {
        // A value too large is refused, not cut down to the cap.
        Assert.False(TryValidate(new SmallPage { PerPage = 500 }, out var results));
        Assert.Equal("PerPage", Assert.Single(Assert.Single(results).MemberNames));
    }

    [Fact]
    public void A_cap_below_the_default_page_size_refuses_the_default()
    {
        // The default PerPage of 50 is above this cap, so a request that keeps the default is refused. A derived request
        // that lowers the cap sets its own default at or below the cap.
        Assert.False(TryValidate(new TinyPage(), out var results));
        Assert.Equal("PerPage", Assert.Single(Assert.Single(results).MemberNames));
    }

    [Fact]
    public void The_largest_page_size_is_500_by_default()
    {
        Assert.True(TryValidate(new PageRequest { PerPage = 500 }, out _));
        Assert.False(TryValidate(new PageRequest { PerPage = 501 }, out _));
    }

    private static bool TryValidate(object request, out List<ValidationResult> results)
    {
        results = [];
        return Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
    }
}
