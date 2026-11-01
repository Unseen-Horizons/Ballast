// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.SqlServer.Tests;

public class SqlBatchesTests
{
    [Theory]
    [InlineData("GO")]
    [InlineData("go")]
    [InlineData("GO;")]
    [InlineData("  GO  ")]
    [InlineData("\tGo ; ")]
    public void A_GO_line_splits_the_script(string separator)
    {
        var batches = SqlBatches.Split($"SELECT 1\n{separator}\nSELECT 2\n");

        Assert.Equal(["SELECT 1\n", "SELECT 2\n"], batches);
    }

    [Fact]
    public void CrLf_input_keeps_the_line_endings_inside_each_batch()
    {
        var batches = SqlBatches.Split("SELECT 1\r\nGO\r\nSELECT 2\r\n");

        Assert.Equal(["SELECT 1\r\n", "SELECT 2\r\n"], batches);
    }

    [Fact]
    public void Consecutive_GO_lines_make_no_empty_batches()
    {
        var batches = SqlBatches.Split("SELECT 1\nGO\nGO\n\nGO\nSELECT 2");

        Assert.Equal(["SELECT 1\n", "SELECT 2"], batches);
    }

    [Fact]
    public void Batches_of_white_space_only_are_dropped()
    {
        var batches = SqlBatches.Split("   \n\t\nGO\nSELECT 1\nGO\n  \r\n");

        Assert.Equal(["SELECT 1\n"], batches);
    }

    [Theory]
    [InlineData("GOTO x")]
    [InlineData("-- GO")]
    [InlineData("SELECT 'GO'")]
    public void A_line_that_only_contains_GO_is_the_separator_and_no_other_line_is(string line)
    {
        var script = $"SELECT 1\n{line}\nSELECT 2";

        Assert.Equal([script], SqlBatches.Split(script));
    }

    [Fact]
    public void Text_without_GO_is_one_batch()
    {
        const string script = "SELECT 1;\nSELECT 2;";

        Assert.Equal([script], SqlBatches.Split(script));
    }

    [Fact]
    public void An_empty_script_has_no_batches()
    {
        Assert.Empty(SqlBatches.Split(string.Empty));
        Assert.Empty(SqlBatches.Split("GO"));
    }

    [Fact]
    public void A_null_script_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => SqlBatches.Split(null!));
    }
}
