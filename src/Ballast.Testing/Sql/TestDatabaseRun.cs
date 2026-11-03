// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace Ballast.Testing.Sql;

/// <summary>
/// The assembly fixture for the databases named by <see cref="TestDatabase.NewName"/>. A test assembly registers it with one line:
/// <c>[assembly: AssemblyFixture(typeof(Ballast.Testing.Sql.TestDatabaseRun))]</c>. The attribute cannot come from this library:
/// xUnit reads it from the test assembly itself.
/// <para>
/// Before the first test it drops the databases a killed run left behind: marked, made by another run, and at least
/// <see cref="TestDatabaseOptions.StaleAfter"/> old by the server's clock. A live run's databases are never that old, so a sweep does
/// not take a database another run is using. After the last test it drops the databases this run still has, so a fixture whose
/// constructor or <c>Dispose</c> failed leaks nothing.
/// </para>
/// <para>
/// Nothing here throws. When SQL Server cannot be reached, or one drop fails, a line on standard error with a <c>[TestDatabase]</c>
/// prefix says what was not done, and the run carries on.
/// </para>
/// </summary>
public sealed class TestDatabaseRun : IAsyncLifetime
{
    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        if (!await ReachableAsync())
            return;

        try
        {
            foreach (var name in await TestDatabase.StaleNamesAsync(TestDatabase.RunId, TestDatabase.StaleAfter))
                await DropOrReportAsync(name);
        }
        catch (Exception ex)
        {
            Report($"the sweep of leftover test databases did not run ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!await ReachableAsync())
            return;

        try
        {
            foreach (var name in await TestDatabase.OwnNamesAsync())
                await DropOrReportAsync(name);
        }
        catch (Exception ex)
        {
            Report($"the end-of-run drop of this run's test databases did not finish ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static async Task DropOrReportAsync(string name)
    {
        try
        {
            await TestDatabase.DropAnyRunAsync(name);
        }
        catch (Exception ex)
        {
            Report($"could not drop '{name}' ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static async Task<bool> ReachableAsync()
    {
        var reason = await TestDatabase.UnreachableReasonAsync();
        if (reason is null)
            return true;

        Report($"SQL Server cannot be reached ({reason}); no test database was swept or dropped.");
        return false;
    }

    private static void Report(string message) => Console.Error.WriteLine($"[TestDatabase] {message}");
}
