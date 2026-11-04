// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.SqlServer.Tests;

/// <summary>The collation check: the probe on a real database, the answers it may give, and a provider with no collation.</summary>
public class CollationCheckTests
{
    [Theory]
    [InlineData(1, CollationResult.CaseInsensitive)]
    [InlineData(0, CollationResult.CaseSensitive)]
    [InlineData(2, CollationResult.CaseSensitive)]
    [InlineData(null, CollationResult.CaseSensitive)]
    public void Evaluate_reads_only_the_answer_1_as_case_insensitive(int? answer, CollationResult expected)
    {
        Assert.Equal(expected, CollationCheck.Evaluate(answer));
    }

    [Fact]
    public void The_probe_compares_the_two_literals_under_the_database_collation()
    {
        Assert.Contains("N'a' = N'A'", CollationCheck.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_case_insensitive_database_is_CaseInsensitive_and_passes()
    {
        await using var database = await SqlTestDatabase.CreateAsync("Latin1_General_100_CI_AS");
        await using var db = SampleContext.Create(database.ConnectionString);

        Assert.Equal(CollationResult.CaseInsensitive, await CollationCheck.CheckAsync(db));
        await CollationCheck.EnsureCaseInsensitiveAsync(db);
    }

    [Fact]
    public async Task A_case_sensitive_database_is_refused_with_the_default_message()
    {
        await using var database = await SqlTestDatabase.CreateAsync("Latin1_General_100_CS_AS");
        await using var db = SampleContext.Create(database.ConnectionString);

        Assert.Equal(CollationResult.CaseSensitive, await CollationCheck.CheckAsync(db));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => CollationCheck.EnsureCaseInsensitiveAsync(db));
        Assert.Equal(CollationCheck.DefaultFailureMessage, thrown.Message);
        Assert.Contains("case-sensitively", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_app_may_give_its_own_failure_message()
    {
        await using var database = await SqlTestDatabase.CreateAsync("Latin1_General_100_CS_AS");
        await using var db = SampleContext.Create(database.ConnectionString);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CollationCheck.EnsureCaseInsensitiveAsync(db, failureMessage: "the app's own words"));

        Assert.Equal("the app's own words", thrown.Message);
    }

    [Fact]
    public async Task A_probe_that_cannot_run_is_Unverified_logged_and_does_not_stop_the_caller()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var missing = new SqlConnectionStringBuilder(database.ConnectionString)
        {
            InitialCatalog = "Ballast_SqlServer_missing" + Guid.NewGuid().ToString("N")[..8],
        }.ConnectionString;
        await using var db = SampleContext.Create(missing);

        Assert.Equal(CollationResult.Unverified, await CollationCheck.CheckAsync(db, NullLogger.Instance));
        await CollationCheck.EnsureCaseInsensitiveAsync(db, NullLogger.Instance);
    }

    [Fact]
    public async Task A_provider_with_no_collation_is_NotRelational_and_is_not_checked()
    {
        var options = new DbContextOptionsBuilder<SampleContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SampleContext(options);

        Assert.Equal(CollationResult.NotRelational, await CollationCheck.CheckAsync(db));
        await CollationCheck.EnsureCaseInsensitiveAsync(db);
    }

    [Fact]
    public async Task A_context_with_no_provider_is_a_configuration_error_that_EF_throws_not_a_classification()
    {
        await using var db = new DbContext(new DbContextOptionsBuilder().Options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CollationCheck.CheckAsync(db));
    }

    [Fact]
    public async Task Null_arguments_are_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CollationCheck.CheckAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => CollationCheck.EnsureCaseInsensitiveAsync(null!));
    }
}
