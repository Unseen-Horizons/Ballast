// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Paging;

namespace Ballast.Paging.Tests;

/// <summary>The row offset a paged list skips by: 1-based pages, never negative, never wrapped.</summary>
public class PagingTests
{
    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(2, 500, 500)]
    [InlineData(3, 20, 40)]
    [InlineData(6, 1, 5)]
    public void The_skip_is_the_rows_before_the_page(int page, int perPage, int expected) =>
        Assert.Equal(expected, Paging.SkipFor(page, perPage));

    [Theory]
    [InlineData(0, 50, "page")]
    [InlineData(-4, 50, "page")]
    [InlineData(int.MinValue, 50, "page")]
    [InlineData(2, 0, "perPage")]
    [InlineData(6, -3, "perPage")]
    public void A_page_or_page_size_below_1_is_refused_and_the_error_names_that_parameter(int page, int perPage, string paramName)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Paging.SkipFor(page, perPage));

        Assert.Equal(paramName, error.ParamName);
    }

    [Fact]
    public void An_absurd_page_skips_to_the_int_ceiling_not_to_a_negative_offset()
    {
        Assert.Equal(int.MaxValue, Paging.SkipFor(int.MaxValue, 500));
        Assert.Equal(int.MaxValue, Paging.SkipFor(int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void The_largest_page_the_request_allows_skips_a_valid_offset()
    {
        Assert.Equal(499_999_500, Paging.SkipFor(PageRequest.MaxPage, 500));
    }
}
