// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace Ballast.Maui.Diagnostics;

/// <summary>
/// The default <see cref="IClientLog"/>: the debugger trace, and the last failures kept in memory for a crash report.
/// </summary>
/// <remarks>
/// A phone app has no logging framework that reaches a release build, so a trace line is what a developer sees with a
/// debugger attached. The failures kept here leave the phone only inside a crash report, and only when the person chooses
/// Send. Nothing here is sent anywhere else.
/// <para>
/// The server trace id is the important part: the APIs stamp every response and every log line for a request with the same
/// id, so keeping it with the failure is what joins "the person saw an error" to the server line that explains why.
/// </para>
/// </remarks>
public sealed class ClientLog : IClientLog
{
    private readonly Action<string> _write;
    private readonly TimeProvider _clock;

    /// <summary>Creates a log.</summary>
    /// <param name="recent">The kept failures. A new log of the default capacity when null.</param>
    /// <param name="write">Where a trace line goes. The debugger trace when null.</param>
    /// <param name="clock">The clock for each kept failure's time. The system clock when null.</param>
    public ClientLog(RecentFailureLog? recent = null, Action<string>? write = null, TimeProvider? clock = null)
    {
        Recent = recent ?? new RecentFailureLog();
        _write = write ?? (line => Trace.WriteLine(line));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The handled failures since the app started, newest last, at most <see cref="RecentFailureLog.Capacity"/>.</summary>
    public RecentFailureLog Recent { get; }

    /// <inheritdoc />
    public void Failure(string source, string summary, string? code = null, string? detail = null, string? traceId = null, Exception? ex = null)
    {
        var line = $"[{source}] {summary}"
            + (code is null ? string.Empty : $" code={code}")
            + (traceId is null ? string.Empty : $" traceId={traceId}")
            + (detail is null ? string.Empty : $" detail={detail}")
            + (ex is null ? string.Empty : $"{Environment.NewLine}{ex}");

        _write(line);

        Recent.Add(RecentFailure.Create(_clock.GetUtcNow().UtcDateTime, source, summary, code, traceId, ex?.GetType().FullName));
    }

    /// <inheritdoc />
    public void Warning(string source, string summary) => _write($"[{source}] WARNING {summary}");
}
