// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.Testing.Fakes;
using Ballast.Testing.Sql;

using Xunit;

namespace Ballast.Testing.Tests;

/// <summary>
/// Runs the example in src/Ballast.Testing/README.md as it is written there. The assembly fixture line the README gives is this
/// test assembly's own, in AssemblyFixtures.cs, and is not repeated here.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        // A database for this test, dropped when the test ends. Skipped when SQL Server is not reachable.
        await using var database = await SqlTestDatabase.CreateAsync("Orders");

        // The payment service is a scripted handler: nothing leaves the machine.
        var payments = ScriptedHttpHandler.FromResponses(HttpAnswers.Text(HttpStatusCode.OK, "paid"));
        using var client = new HttpClient(payments);

        var answer = await client.GetStringAsync("https://payments.example.test/charge");

        Assert.Equal("paid", answer);
        Assert.Single(payments.Requests);
        Assert.False(string.IsNullOrEmpty(database.ConnectionString));
    }
}
