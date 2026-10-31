// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

using Ballast.Paging;

namespace Ballast.Paging.Tests;

/// <summary>The paged result: its JSON names, its round trip, and its page boundaries.</summary>
public class PagedResultTests
{
    private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);

    private static readonly string[] _members = ["data", "hasNextPage", "hasPreviousPage", "page", "perPage", "total", "totalPages"];

    [Fact]
    public void Round_trips_through_System_Text_Json_in_both_directions()
    {
        var original = new PagedResult<string>(["a", "b"], total: 7, new PageRequest { Page = 2, PerPage = 2 });

        var json = JsonSerializer.Serialize(original, _web);
        var restored = JsonSerializer.Deserialize<PagedResult<string>>(json, _web);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.Data.Count);
        Assert.Equal("a", restored.Data[0]);
        Assert.Equal("b", restored.Data[1]);
        Assert.Equal(7, restored.Total);
        Assert.Equal(2, restored.Page);
        Assert.Equal(2, restored.PerPage);
        Assert.Equal(4, restored.TotalPages);
        Assert.True(restored.HasNextPage);
        Assert.True(restored.HasPreviousPage);
    }

    [Fact]
    public void The_json_has_exactly_the_members_the_apps_read()
    {
        var json = JsonSerializer.Serialize(new PagedResult<string>(["a"], total: 10, new PageRequest { Page = 2, PerPage = 5 }), _web);

        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(_members, names);
    }

    [Fact]
    public void The_json_carries_the_paging_flags_the_admin_pages_read()
    {
        var json = JsonSerializer.Serialize(new PagedResult<string>(["a"], total: 10, new PageRequest { Page = 2, PerPage = 5 }), _web);

        Assert.Contains("hasNextPage", json, StringComparison.Ordinal);
        Assert.Contains("hasPreviousPage", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_a_result_ignores_the_computed_flags_in_the_json()
    {
        var json = """{"data":["a"],"total":10,"page":2,"perPage":5,"totalPages":2,"hasNextPage":false,"hasPreviousPage":true}""";

        var restored = JsonSerializer.Deserialize<PagedResult<string>>(json, _web);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.TotalPages);
        Assert.False(restored.HasNextPage);
        Assert.True(restored.HasPreviousPage);
    }

    [Theory]
    [InlineData(0, 20, 1, false, false)]
    [InlineData(10, 20, 1, false, false)]
    [InlineData(41, 20, 1, true, false)]
    [InlineData(41, 20, 2, true, true)]
    [InlineData(41, 20, 3, false, true)]
    public void Computes_page_boundaries(int total, int perPage, int page, bool hasNext, bool hasPrevious)
    {
        var result = new PagedResult<string>([], total, new PageRequest { Page = page, PerPage = perPage });

        Assert.Equal(hasNext, result.HasNextPage);
        Assert.Equal(hasPrevious, result.HasPreviousPage);
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(41, 20, 3)]
    [InlineData(40, 20, 2)]
    [InlineData(int.MaxValue, 1, int.MaxValue)]
    public void The_number_of_pages_is_the_total_divided_by_the_page_size_rounded_up(int total, int perPage, int totalPages)
    {
        var result = new PagedResult<string>([], total, new PageRequest { PerPage = perPage });

        Assert.Equal(totalPages, result.TotalPages);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    public void A_page_below_1_is_refused(int page, int perPage)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<string>([], 10, new PageRequest { Page = page, PerPage = perPage }));

        Assert.Equal("request", error.ParamName);
        Assert.Contains("request.Page", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -3)]
    public void A_page_size_below_1_is_refused_so_the_page_count_never_divides_by_zero(int page, int perPage)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<string>([], 10, new PageRequest { Page = page, PerPage = perPage }));

        Assert.Equal("request", error.ParamName);
        Assert.Contains("request.PerPage", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_past_the_last_one_is_empty_with_the_correct_total()
    {
        var result = new PagedResult<string>([], total: 3, new PageRequest { Page = 5, PerPage = 2 });

        Assert.Empty(result.Data);
        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.TotalPages);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void The_request_gives_the_page_and_the_page_size()
    {
        var result = new PagedResult<string>(["a"], total: 9, new PageRequest { Page = 3, PerPage = 4 });

        Assert.Equal(3, result.Page);
        Assert.Equal(4, result.PerPage);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public void The_five_value_constructor_stores_its_values_as_given()
    {
        var result = new PagedResult<string>(["a"], 9, 3, 4, 7);

        Assert.Equal(9, result.Total);
        Assert.Equal(3, result.Page);
        Assert.Equal(4, result.PerPage);
        Assert.Equal(7, result.TotalPages);
    }

    [Fact]
    public void The_rows_are_copied_so_a_later_change_to_the_source_does_not_show()
    {
        var source = new List<string> { "a" };
        var result = new PagedResult<string>(source, 1, new PageRequest());

        source.Add("b");

        Assert.Equal("a", Assert.Single(result.Data));
    }

    [Fact]
    public void A_negative_total_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<string>([], -1, new PageRequest()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<string>([], -1, 1, 50, 1));
    }

    [Fact]
    public void Null_rows_or_a_null_request_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new PagedResult<string>(null!, 1, new PageRequest()));
        Assert.Throws<ArgumentNullException>(() => new PagedResult<string>([], 1, null!));
        Assert.Throws<ArgumentNullException>(() => new PagedResult<string>(null!, 1, 1, 50, 1));
    }
}
