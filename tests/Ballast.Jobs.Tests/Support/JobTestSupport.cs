// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Jobs.Tests;

/// <summary>
/// Waits, in real time, for something the job-lock tests started on another thread. Every wait has a limit and says
/// what it was waiting for when it runs out, so a hang is a failure with a reason and not a stuck run.
/// </summary>
internal static class JobWait
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > Limit)
            {
                Assert.Fail($"Timed out after {Limit.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s waiting for: {what}");
            }

            await Task.Delay(5);
        }
    }
}

/// <summary>An <see cref="IAppLockSession"/> whose answers the test decides, and which remembers what was done to it.</summary>
internal sealed class ScriptedLockSession : IAppLockSession
{
    private readonly string _name;
    private readonly ConcurrentQueue<string> _events;
    private int _acquireCalls;
    private int _releaseCalls;
    private int _disposeCalls;
    private volatile bool _holds;
    private Exception? _holdsFails;

    public ScriptedLockSession(string name, ConcurrentQueue<string> events)
    {
        _name = name;
        _events = events;
    }

    public string Name => _name;

    /// <summary>The answer to the acquire call: false is "another copy holds the lock".</summary>
    public bool Grants { get; set; } = true;

    /// <summary>Opening the connection fails with this.</summary>
    public Exception? OpenFails { get; set; }

    /// <summary>The acquire call fails with this.</summary>
    public Exception? AcquireFails { get; set; }

    /// <summary>The heartbeat query fails with this. May be set while the session is held.</summary>
    public Exception? HoldsFails
    {
        get => Volatile.Read(ref _holdsFails);
        set => Volatile.Write(ref _holdsFails, value);
    }

    /// <summary>When set, opening waits until the test completes it.</summary>
    public TaskCompletionSource? OpenGate { get; set; }

    /// <summary>When set, the acquire call waits until the test completes it with its answer.</summary>
    public TaskCompletionSource<bool>? AcquireGate { get; set; }

    /// <summary>
    /// The acquire call keeps waiting for <see cref="AcquireGate"/> when the caller cancels, as a request that has already
    /// reached the server can still be granted at the moment the client gives up.
    /// </summary>
    public bool AcquireIgnoresCancellation { get; set; }

    /// <summary>What the heartbeat query reports. Set to false while the session is held to make the lock "disappear".</summary>
    public bool Holds
    {
        get => _holds;
        set => _holds = value;
    }

    public int AcquireCalls => Volatile.Read(ref _acquireCalls);

    public int ReleaseCalls => Volatile.Read(ref _releaseCalls);

    public bool Disposed => Volatile.Read(ref _disposeCalls) > 0;

    public async Task OpenAsync(CancellationToken ct)
    {
        _events.Enqueue(_name + ":open");
        if (OpenGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        if (OpenFails is { } failure)
        {
            throw failure;
        }
    }

    public async Task<bool> TryAcquireAsync(string resource, TimeSpan timeout, CancellationToken ct)
    {
        Interlocked.Increment(ref _acquireCalls);
        _events.Enqueue(_name + ":acquire");
        if (AcquireFails is { } failure)
        {
            throw failure;
        }

        var granted = Grants;
        if (AcquireGate is { } gate)
        {
            granted = AcquireIgnoresCancellation ? await gate.Task : await gate.Task.WaitAsync(ct);
        }

        _holds = granted;
        return granted;
    }

    public Task<bool> HoldsAsync(string resource, CancellationToken ct)
    {
        if (HoldsFails is { } failure)
        {
            return Task.FromException<bool>(failure);
        }

        return Task.FromResult(_holds);
    }

    public Task ReleaseAsync(string resource, CancellationToken ct)
    {
        Interlocked.Increment(ref _releaseCalls);
        _events.Enqueue(_name + ":release");
        _holds = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCalls);
        _events.Enqueue(_name + ":dispose");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The session factory the leadership service is given: the sessions the test prepared, in order, and then sessions that
/// always find the lock busy. Every call is written to <see cref="Events"/> in the order it happened.
/// </summary>
internal sealed class SessionScript
{
    private readonly ConcurrentQueue<ScriptedLockSession> _prepared = new();
    private readonly ConcurrentQueue<ScriptedLockSession> _created = new();
    private int _count;

    public ConcurrentQueue<string> Events { get; } = new();

    public int CreatedCount => _created.Count;

    /// <summary>Prepares the session the next attempt will get. It is named s1, s2 and so on in the order prepared.</summary>
    public ScriptedLockSession Enqueue()
    {
        var session = NewSession();
        _prepared.Enqueue(session);
        return session;
    }

    public IAppLockSession Create()
    {
        if (!_prepared.TryDequeue(out var session))
        {
            session = NewSession();
            session.Grants = false;
        }

        _created.Enqueue(session);
        return session;
    }

    private ScriptedLockSession NewSession() =>
        new("s" + Interlocked.Increment(ref _count).ToString(CultureInfo.InvariantCulture), Events);
}

/// <summary>An application lifetime the test can stop.</summary>
internal sealed class StubLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => _stopping.Token;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication() => _stopping.Cancel();

    public void Dispose() => _stopping.Dispose();
}

internal sealed class DelegateTermHook : IJobTermHook
{
    private readonly Func<CancellationToken, Task> _run;

    public DelegateTermHook(Func<CancellationToken, Task> run) { _run = run; }

    public Task OnTermStartAsync(CancellationToken ct) => _run(ct);
}

/// <summary>A leadership with a holder flag the test flips, for the code that only reads <see cref="IJobLeadership.IsHolder"/>.</summary>
internal sealed class StubLeadership : IJobLeadership
{
    public StubLeadership(bool isHolder) { IsHolder = isHolder; }

    public bool Enabled => true;

    public bool IsHolder { get; set; }

    public DateTime? HolderSinceUtc => null;

    public DateTime? LastAttemptUtc => null;

    public JobLeadershipOutcome LastOutcome => IsHolder ? JobLeadershipOutcome.Acquired : JobLeadershipOutcome.HeldByOther;

    public string? LastError => null;

    public DateTime StartedUtc => DateTime.UnixEpoch;

    public Task<CancellationToken> WaitForLeadershipAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task RunAsLeaderAsync(Func<CancellationToken, Task> work, bool once, CancellationToken ct) => throw new NotSupportedException();
}

internal readonly record struct JobLogEntry(LogLevel Level, string Message);

/// <summary>Keeps what was logged, formatted, so a test can say a Warning was written and what it said.</summary>
internal sealed class JobTestLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<JobLogEntry> _entries = new();

    public IReadOnlyList<JobLogEntry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue(new JobLogEntry(logLevel, formatter(state, exception)));
}

/// <summary>
/// A real <see cref="JobLeadership"/> over scripted lock sessions, on the system clock with intervals of a few
/// milliseconds. Disposing it stops the application first, as the host does.
/// </summary>
internal sealed class LeaderRig : IAsyncDisposable
{
    private readonly StubLifetime _lifetime = new();

    public LeaderRig(IEnumerable<IJobTermHook>? hooks = null, Action<JobLeadershipOptions>? tune = null)
    {
        var options = new JobLeadershipOptions
        {
            Enabled = true,
            LockTimeout = TimeSpan.FromMilliseconds(50),
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            BusyRetryDelay = TimeSpan.FromMilliseconds(20),
            ErrorRetryDelay = TimeSpan.FromMilliseconds(20),
            ReleaseTimeout = TimeSpan.FromSeconds(2),
        };
        tune?.Invoke(options);

        var services = new ServiceCollection();
        foreach (var hook in hooks ?? [])
        {
            services.AddSingleton(hook);
        }

        Leadership = new JobLeadership(
            options, "test:jobs", Sessions.Create, _lifetime, services.BuildServiceProvider(), TimeProvider.System, Log);
    }

    public SessionScript Sessions { get; } = new();

    public JobTestLogger<JobLeadership> Log { get; } = new();

    public StubLifetime Lifetime => _lifetime;

    public JobLeadership Leadership { get; }

    public Task StartAsync() => Leadership.StartAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        _lifetime.StopApplication();
        await Leadership.StopAsync(CancellationToken.None);
        Leadership.Dispose();
        _lifetime.Dispose();
    }
}
