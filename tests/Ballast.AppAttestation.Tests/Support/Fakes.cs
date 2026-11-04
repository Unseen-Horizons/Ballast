// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace Ballast.AppAttestation.Tests.Support;

/// <summary>A clock the test moves by hand.</summary>
public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset? start = null) { _now = start ?? new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero); }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>A decoder that returns a fixture, or fails the way Google failing would. It never calls Google.</summary>
public sealed class FakeDecoder : IPlayIntegrityDecoder
{
    public IntegrityVerdict? Verdict { get; set; }
    public Exception? Failure { get; set; }
    public int Calls { get; private set; }

    /// <summary>Called with the token, so a verdict can answer the nonce the test minted.</summary>
    public Func<string, IntegrityVerdict>? ForToken { get; set; }

    public Task<IntegrityVerdict> DecodeAsync(string integrityToken, CancellationToken ct = default)
    {
        Calls++;
        if (Failure is not null)
            throw Failure;
        if (ForToken is not null)
            return Task.FromResult(ForToken(integrityToken));
        return Task.FromResult(Verdict ?? throw new InvalidOperationException("No verdict set."));
    }
}

/// <summary>Verdict fixtures for the app <see cref="AppAttestationTestHost.Package"/>.</summary>
public static class Verdicts
{
    public static IntegrityVerdict PlayRecognised(string nonce) => new(
        AppAttestationTestHost.Package, nonce, "PLAY_RECOGNIZED", AppAttestationTestHost.Package, ["Zm9vYmFyYmF6cXV4Zm9vYmFyYmF6cXV4Zm9vYmFyYmE"], ["MEETS_DEVICE_INTEGRITY"]);

    public static IntegrityVerdict Sideloaded(string nonce, params string[] certificates) => new(
        AppAttestationTestHost.Package, nonce, "UNRECOGNIZED_VERSION", AppAttestationTestHost.Package, certificates, ["MEETS_DEVICE_INTEGRITY"]);
}

/// <summary>
/// Collects log lines so a test can say what was (and was not) written. A request can log after its response has gone back, on
/// another thread, while the test is reading; a concurrent queue lets a read take a snapshot without a lock.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);

    public void Dispose() { }

    private sealed class Capture(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)}");
    }
}
