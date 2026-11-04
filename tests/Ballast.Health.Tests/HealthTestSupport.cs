// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Ballast.Health.Tests;

/// <summary>A clock the test moves by hand. It starts at a fixed instant, so every assertion about a window is exact.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>A logger that keeps what it was given, so a test can assert on the readiness log.</summary>
internal sealed class CapturingLogger : ILogger
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, formatter(state, exception)));
}

/// <summary>A logger provider that keeps every line with its category, so a test can see which category the lines went under.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Forwarder(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Forwarder : ILogger
    {
        private readonly CapturingLoggerProvider _provider;
        private readonly string _category;

        public Forwarder(CapturingLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _provider.Entries.Enqueue((_category, logLevel, formatter(state, exception)));
    }
}

/// <summary>Answers the database question the test decides, and counts how often it was asked.</summary>
internal sealed class FakeReachability : IDatabaseReachability
{
    public Func<CancellationToken, Task<bool>> Answer { get; set; } = _ => Task.FromResult(true);

    public int Asks;

    public Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref Asks);
        return Answer(ct);
    }
}

/// <summary>Builds a minimal web host on an in-memory test server, for the endpoint tests.</summary>
internal static class TestHosts
{
    /// <summary>
    /// Builds an application on the test server, lets <paramref name="configure"/> set its services and logging, maps its routes
    /// with <paramref name="map"/>, starts it (which starts the warm-up, as the port would), and runs <paramref name="probe"/>.
    /// </summary>
    public static async Task RunAsync(Action<WebApplicationBuilder> configure, Action<WebApplication> map, Func<WebApplication, HttpClient, Task> probe)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        configure(builder);

        await using var app = builder.Build();
        map(app);
        await app.StartAsync();
        await probe(app, app.GetTestClient());
    }
}
