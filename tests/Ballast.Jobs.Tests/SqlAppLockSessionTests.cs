// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Jobs.Tests;

/// <summary>
/// The SQL Server application lock itself, with two real sessions. Each test makes a database of its own and drops it at the end,
/// so nothing is shared with another run or with a running app. The tests are skipped when SQL Server cannot be reached.
/// </summary>
public class SqlAppLockSessionTests
{
    private static string NewResource() => "Ballast.Jobs.Tests:jobs:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task The_lock_is_exclusive_between_two_sessions_and_free_again_after_release()
    {
        var reason = await SqlTestDatabase.UnreachableReasonAsync();
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        await using var database = await TemporaryDatabase.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var resource = NewResource();
        await using var first = new SqlAppLockSession(database.ConnectionString, "job-lock-test-1");
        await using var second = new SqlAppLockSession(database.ConnectionString, "job-lock-test-2");
        await first.OpenAsync(ct);
        await second.OpenAsync(ct);

        Assert.True(await first.TryAcquireAsync(resource, TimeSpan.FromSeconds(2), ct));
        Assert.True(await first.HoldsAsync(resource, ct));
        Assert.False(await second.HoldsAsync(resource, ct));

        // Busy is an answer, not an error: the second session waits out its timeout and is told no.
        Assert.False(await second.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(200), ct));
        Assert.False(await second.HoldsAsync(resource, ct));

        await first.ReleaseAsync(resource, ct);
        Assert.False(await first.HoldsAsync(resource, ct));

        Assert.True(await second.TryAcquireAsync(resource, TimeSpan.FromSeconds(2), ct));
        Assert.True(await second.HoldsAsync(resource, ct));
    }

    [Fact]
    public async Task Closing_the_connection_frees_the_lock_for_the_next_session()
    {
        var reason = await SqlTestDatabase.UnreachableReasonAsync();
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        await using var database = await TemporaryDatabase.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var resource = NewResource();
        await using var first = new SqlAppLockSession(database.ConnectionString, "job-lock-test-1");
        await using var second = new SqlAppLockSession(database.ConnectionString, "job-lock-test-2");
        await first.OpenAsync(ct);
        await second.OpenAsync(ct);
        Assert.True(await first.TryAcquireAsync(resource, TimeSpan.FromSeconds(2), ct));
        Assert.False(await second.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(200), ct));

        // The connection is not pooled, so closing it ends the session, and the session's locks go with it.
        // (Disposing a second time at the end of the test is harmless.)
        await first.DisposeAsync();

        Assert.True(await second.TryAcquireAsync(resource, TimeSpan.FromSeconds(10), ct));
        Assert.True(await second.HoldsAsync(resource, ct));
    }

    [Fact]
    public async Task A_session_that_never_took_the_lock_does_not_report_holding_it()
    {
        var reason = await SqlTestDatabase.UnreachableReasonAsync();
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        await using var database = await TemporaryDatabase.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var session = new SqlAppLockSession(database.ConnectionString, "job-lock-test-1");
        await session.OpenAsync(ct);

        Assert.False(await session.HoldsAsync(NewResource(), ct));
    }

    [Fact]
    public async Task The_job_lock_takes_the_configured_resource_on_a_connection_named_with_the_suffix()
    {
        var reason = await SqlTestDatabase.UnreachableReasonAsync();
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        await using var database = await TemporaryDatabase.CreateAsync();
        using var lifetime = new StubLifetime();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        services.AddJobLeadership(
            Settings.From(("JobLeadership:Enabled", "true"), ("JobLeadership:ResourceName", "Shop:jobs")),
            "Shop:other",
            database.ConnectionString,
            setup => setup.ApplicationNameSuffix = "-probe");
        await using var provider = services.BuildServiceProvider();
        var leadership = provider.GetRequiredService<IJobLeadership>();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(CancellationToken.None);
        await JobWait.UntilAsync(() => leadership.IsHolder, "the copy takes the lock");

        // Another connection finds the resource the configuration names busy, and the copy's connection shows its suffix.
        await using var probe = new SqlConnection(database.ConnectionString);
        await probe.OpenAsync();
        Assert.Equal(-1, await GrantAtOnceAsync(probe, "Shop:jobs"));
        Assert.Equal(1, await CountSessionsNamedAsync(probe, "Shop:jobs-probe"));

        await hosted.StopAsync(CancellationToken.None);

        // Released when the copy stops: the probe can take it now.
        Assert.True(await GrantAtOnceAsync(probe, "Shop:jobs") >= 0);
    }

    /// <summary>Asks for the resource without waiting: 0 or 1 when granted, -1 when another session holds it.</summary>
    private static async Task<int> GrantAtOnceAsync(SqlConnection connection, string resource)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @Name, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@Name", resource));
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    /// <summary>How many sessions on the server carry this application name.</summary>
    private static async Task<int> CountSessionsNamedAsync(SqlConnection connection, string applicationName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE program_name = @Name;";
        command.Parameters.Add(new SqlParameter("@Name", applicationName));
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
