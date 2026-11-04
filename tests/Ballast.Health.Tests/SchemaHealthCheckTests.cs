// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ballast.Health.Tests;

/// <summary>The schema check against a fake migration source: what the code ships, what the database has applied, and how often it asks.</summary>
public class SchemaHealthCheckTests
{
    private sealed class FakeSource : IMigrationSource
    {
        public IReadOnlyCollection<string> Code { get; set; } = [];

        public IReadOnlyCollection<string> Applied { get; set; } = [];

        public Exception? Fails { get; set; }

        /// <summary>When set, the read waits until the test completes it, so two checks can be started together.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public int Calls;

        public async Task<MigrationReadout> ReadAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(ct);
            }
            if (Fails is not null)
            {
                throw Fails;
            }
            return new MigrationReadout(Code, Applied);
        }
    }

    private static SchemaHealthCheck Check(FakeSource source, ManualTime time) =>
        new(source, Options.Create(new HealthOptions()), time, NullLogger<SchemaHealthCheck>.Instance);

    private static async Task<HealthCheckResult> RunAsync(SchemaHealthCheck check) =>
        await check.CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Pending_migrations_are_unhealthy()
    {
        var source = new FakeSource { Code = ["a", "b"], Applied = ["a"] };

        var result = await RunAsync(Check(source, new ManualTime()));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task A_complete_schema_is_healthy()
    {
        var source = new FakeSource { Code = ["a", "b"], Applied = ["a", "b"] };

        var result = await RunAsync(Check(source, new ManualTime()));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task A_database_ahead_of_the_code_is_still_healthy()
    {
        // A rollback leaves migrations the code does not ship with. The code needs only what it ships.
        var source = new FakeSource { Code = ["a"], Applied = ["a", "z-from-a-newer-build"] };

        var result = await RunAsync(Check(source, new ManualTime()));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Once_current_the_database_is_never_asked_again()
    {
        var source = new FakeSource { Code = ["a"], Applied = ["a"] };
        var check = Check(source, new ManualTime());

        await RunAsync(check);
        await RunAsync(check);
        await RunAsync(check);

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task A_pending_schema_is_rechecked_only_after_the_recheck_interval()
    {
        var time = new ManualTime();
        var source = new FakeSource { Code = ["a"], Applied = [] };
        var check = Check(source, time);

        await RunAsync(check);
        time.Advance(TimeSpan.FromSeconds(1));
        await RunAsync(check);
        Assert.Equal(1, source.Calls);

        time.Advance(TimeSpan.FromSeconds(6));
        await RunAsync(check);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Concurrent_checks_ask_the_database_once()
    {
        var gate = new TaskCompletionSource();
        var source = new FakeSource { Code = ["a"], Applied = [], Gate = gate };
        var check = Check(source, new ManualTime());

        var first = RunAsync(check);
        var second = RunAsync(check);
        await Task.Delay(50);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, source.Calls);
        Assert.Equal(HealthStatus.Unhealthy, (await first).Status);
    }

    [Fact]
    public async Task A_source_that_throws_is_unhealthy_and_its_message_is_not_reported()
    {
        var source = new FakeSource { Fails = new InvalidOperationException("Server=db.internal;Password=hunter2") };

        var result = await RunAsync(Check(source, new ManualTime()));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("hunter2", result.Description ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(result.Exception);
    }
}
