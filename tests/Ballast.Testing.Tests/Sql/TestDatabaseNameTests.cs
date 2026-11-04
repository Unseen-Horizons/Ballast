// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Sql;

namespace Ballast.Testing.Tests.Sql;

/// <summary>The names of test databases. None of these tests needs SQL Server.</summary>
public class TestDatabaseNameTests
{
    [Fact]
    public void A_new_name_carries_the_purpose_the_marker_and_this_run_id()
    {
        var name = TestDatabase.NewName("Orders");

        Assert.StartsWith("Orders" + TestDatabase.NameMarker, name, StringComparison.Ordinal);
        Assert.Contains(TestDatabase.RunId, name, StringComparison.Ordinal);
        Assert.Equal(TestDatabase.RunId, TestDatabase.RunIdOf(name));
        Assert.True(TestDatabase.IsOwnName(name));
    }

    [Fact]
    public void Each_new_name_is_different()
    {
        var first = TestDatabase.NewName("Same");
        var second = TestDatabase.NewName("Same");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1Orders")]
    [InlineData("Order_s")]
    [InlineData("Order s")]
    [InlineData("OrdersWithAPurposeNameThatIsFarTooLongToBeReadableInAServerListAtAllxxxxxxxxxxxxxxxxxxxxxx")]
    public void A_purpose_that_is_not_letters_and_digits_starting_with_a_letter_is_refused(string purpose)
    {
        Assert.Throws<ArgumentException>(() => TestDatabase.NewName(purpose));
    }

    [Fact]
    public void A_null_purpose_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TestDatabase.NewName(null!));
    }

    [Fact]
    public void A_name_from_another_run_is_not_this_runs_and_has_its_own_run_id()
    {
        var foreign = "Orders" + TestDatabase.NameMarker + "0123456789ab_7";

        Assert.Equal("0123456789ab", TestDatabase.RunIdOf(foreign));
        Assert.False(TestDatabase.IsOwnName(foreign));
    }

    [Theory]
    [InlineData("Orders")]
    [InlineData("GetThereDB")]
    [InlineData("Orders_btt")]
    [InlineData("Orders_btt0123456789ab")]
    [InlineData("Orders_btt0123456789AB_1")]
    [InlineData("Orders_btt0123456789ab_x")]
    [InlineData("Orders_gtt0123456789ab_1")]
    public void A_name_that_is_not_a_run_name_has_no_run_id(string name)
    {
        Assert.Null(TestDatabase.RunIdOf(name));
        Assert.False(TestDatabase.IsOwnName(name));
    }

    [Fact]
    public void A_fixed_name_is_refused_before_any_server_is_reached()
    {
        var error = Assert.Throws<ArgumentException>(() => TestDatabase.ConnectionStringFor("GetThereDB"));
        Assert.Contains("TestDatabase.NewName", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creating_a_database_under_a_fixed_name_is_refused_before_any_server_is_reached()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TestDatabase.CreateEmptyAsync("TransitGraphDB"));
    }

    [Fact]
    public async Task Dropping_a_database_under_a_fixed_name_is_refused_before_any_server_is_reached()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TestDatabase.DropAsync("TransitGraphDB"));
    }

    [Fact]
    public async Task A_drop_of_any_run_refuses_a_name_that_is_not_a_run_name()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TestDatabase.DropAnyRunAsync("master"));
    }

    [Fact]
    public async Task A_plain_collation_name_is_required()
    {
        var name = TestDatabase.NewName("Collate");

        await Assert.ThrowsAsync<ArgumentException>(() => TestDatabase.CreateEmptyAsync(name, "Latin1'; DROP DATABASE x; --"));
    }

    [Fact]
    public void The_settings_are_fixed_once_a_name_is_read_so_a_later_configure_is_refused()
    {
        _ = TestDatabase.NameMarker;

        Assert.Throws<InvalidOperationException>(() => TestDatabase.Configure(new TestDatabaseOptions()));
    }

    [Theory]
    [InlineData("_")]
    [InlineData("btt")]
    [InlineData("_b_t")]
    [InlineData("_bttbttbttbttbttbtt")]
    [InlineData("_b1")]
    public void A_marker_that_is_not_an_underscore_and_letters_is_refused(string marker)
    {
        Assert.Throws<ArgumentException>(() => TestDatabase.Configure(new TestDatabaseOptions { NameMarker = marker }));
    }

    [Fact]
    public void A_blank_connection_variable_or_default_connection_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TestDatabase.Configure(new TestDatabaseOptions { ConnectionVariable = " " }));
        Assert.Throws<ArgumentException>(() => TestDatabase.Configure(new TestDatabaseOptions { DefaultConnection = string.Empty }));
    }

    [Fact]
    public void A_leftover_age_that_is_not_above_zero_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TestDatabase.Configure(new TestDatabaseOptions { StaleAfter = TimeSpan.Zero }));
    }

    [Fact]
    public void The_default_options_name_the_ballast_variable_and_the_btt_marker()
    {
        var options = new TestDatabaseOptions();

        Assert.Equal("BALLAST_TEST_SQL", options.ConnectionVariable);
        Assert.Equal("_btt", options.NameMarker);
        Assert.Equal(TimeSpan.FromHours(24), options.StaleAfter);
    }
}
