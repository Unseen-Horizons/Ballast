# Ballast.Testing

Test plumbing for xUnit v3 projects: SQL Server test databases that several runs can share, the sweep of the ones a killed run
left behind, captured logs, scripted HTTP fakes, a loopback server, SQL exception builders, in-memory and API test hosts, and the
source guard kit with its migration-safety engine.

Install it in a test project (it is a test-only package):

```
dotnet add package Ballast.Testing --prerelease
```

## Example

```csharp
using System.Net;

using Ballast.Testing.Fakes;
using Ballast.Testing.Sql;

using Xunit;

// In the test project, once: the fixture that sweeps leftovers and drops this run's databases.
[assembly: AssemblyFixture(typeof(TestDatabaseRun))]

public class OrderTests
{
    [Fact]
    public async Task An_order_is_stored_and_the_payment_service_is_asked_once()
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
```

The database is named by `TestDatabase.NewName`, so two test runs on one SQL Server never drop each other's databases. The
server is read from the `BALLAST_TEST_SQL` environment variable, and falls back to the local Windows server.

The usage guide, with every option and every way the library fails, is `docs/Testing.md` in the repository.
