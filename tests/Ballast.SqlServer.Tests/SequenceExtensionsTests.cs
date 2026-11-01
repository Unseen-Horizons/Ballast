// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ballast.SqlServer.Tests;

/// <summary>The sequence read: each call takes the next value, a schema-qualified name works, and a name that is not an identifier is refused before any SQL.</summary>
public class SequenceExtensionsTests
{
    /// <summary>A server that is never reached: the name checks must refuse before a connection is attempted.</summary>
    private static SampleContext UnreachableContext() =>
        SampleContext.Create("Server=unreachable.invalid;Database=none;Trusted_Connection=True;Connect Timeout=1");

    [Fact]
    public async Task Each_call_takes_the_next_value_of_the_sequence()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = SampleContext.Create(database.ConnectionString);
        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE dbo.Tags AS bigint START WITH 5 INCREMENT BY 1");

        Assert.Equal(5L, await db.NextSequenceValueAsync<long>("Tags"));
        Assert.Equal(6L, await db.NextSequenceValueAsync<long>("Tags"));
        Assert.Equal(7L, await db.NextSequenceValueAsync<long>("dbo.Tags"));
    }

    [Fact]
    public async Task A_schema_qualified_name_reads_the_sequence_of_that_schema()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = SampleContext.Create(database.ConnectionString);
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA Audit");
        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE Audit.Numbers AS int START WITH 10 INCREMENT BY 1");

        Assert.Equal(10, await db.NextSequenceValueAsync<int>("Audit.Numbers"));
    }

    [Fact]
    public async Task A_sequence_that_does_not_exist_is_an_error_not_a_guess()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = SampleContext.Create(database.ConnectionString);

        var thrown = await Assert.ThrowsAsync<SqlException>(() => db.NextSequenceValueAsync<long>("NoSuchSequence"));

        Assert.Equal(208, thrown.Number); // Invalid object name
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Tags; DROP TABLE Orders")]
    [InlineData("Tags]")]
    [InlineData("[Tags]")]
    [InlineData("Tags-x")]
    [InlineData("1Tags")]
    [InlineData("a..b")]
    [InlineData("a.b.c")]
    [InlineData(".Tags")]
    [InlineData("Tags.")]
    [InlineData("Tags ")]
    [InlineData("Tags'")]
    [InlineData("Ünicode")]
    [InlineData("x.Ünicode")]
    public async Task A_name_that_is_not_a_plain_identifier_is_refused_before_any_SQL_is_sent(string name)
    {
        await using var db = UnreachableContext();

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => db.NextSequenceValueAsync<long>(name));

        Assert.Equal("sequenceName", thrown.ParamName);
    }

    [Fact]
    public async Task A_name_of_more_than_128_characters_is_refused()
    {
        await using var db = UnreachableContext();

        var name = new string('a', 129);
        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => db.NextSequenceValueAsync<long>(name));

        Assert.Equal("sequenceName", thrown.ParamName);
    }

    [Fact]
    public async Task A_null_context_or_name_is_refused()
    {
        await using var db = UnreachableContext();

        await Assert.ThrowsAsync<ArgumentNullException>(() => ((DbContext)null!).NextSequenceValueAsync<long>("Tags"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.NextSequenceValueAsync<long>(null!));
    }
}
