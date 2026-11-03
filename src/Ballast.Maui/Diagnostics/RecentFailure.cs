// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Diagnostics;

/// <summary>
/// One handled failure, kept in memory so a crash report can say what had already gone wrong before the crash. Deliberately
/// small: the source, a short developer sentence, the code, the server's trace id, the time and the exception's type name. It
/// carries no message and no detail, because those are where a request address with a position in it, or something the person
/// typed, ends up.
/// </summary>
/// <param name="TimeUtc">When the failure was recorded, in UTC.</param>
/// <param name="Source">Where it happened, scrubbed and capped.</param>
/// <param name="Summary">The developer sentence, scrubbed and capped.</param>
/// <param name="Code">The stable error code, scrubbed and capped, or null.</param>
/// <param name="TraceId">The server's trace id, scrubbed and capped, or null.</param>
/// <param name="ExceptionType">The exception's type name, scrubbed and capped, or null.</param>
public sealed record RecentFailure(
    DateTime TimeUtc,
    string Source,
    string Summary,
    string? Code,
    string? TraceId,
    string? ExceptionType)
{
    /// <summary>The longest summary kept. It is a developer sentence; anything longer is not one.</summary>
    public const int MaxSummaryLength = 100;

    /// <summary>The longest source, code, trace id or type name kept.</summary>
    public const int MaxFieldLength = 80;

    /// <summary>
    /// A failure with every free-text field scrubbed (<see cref="CrashReportText.Scrub"/>) and capped, so what is stored is already
    /// safe to put in a report.
    /// </summary>
    /// <param name="timeUtc">When it happened, in UTC.</param>
    /// <param name="source">Where it happened.</param>
    /// <param name="summary">The developer sentence.</param>
    /// <param name="code">The stable error code, or null.</param>
    /// <param name="traceId">The server's trace id, or null.</param>
    /// <param name="exceptionType">The exception's type name, or null.</param>
    /// <returns>The failure, ready to keep.</returns>
    public static RecentFailure Create(DateTime timeUtc, string source, string summary, string? code, string? traceId, string? exceptionType) =>
        new(
            timeUtc,
            CrashReportText.Clip(CrashReportText.Scrub(source), MaxFieldLength),
            CrashReportText.Clip(CrashReportText.Scrub(summary), MaxSummaryLength),
            Field(code),
            Field(traceId),
            Field(exceptionType));

    private static string? Field(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : CrashReportText.Clip(CrashReportText.Scrub(value.Trim()), MaxFieldLength);
}

/// <summary>
/// The last <see cref="Capacity"/> handled failures, oldest dropped first. Thread-safe: failures are logged from view models, HTTP
/// callbacks and platform receivers alike.
/// </summary>
public sealed class RecentFailureLog
{
    /// <summary>How many failures are kept by default.</summary>
    public const int Capacity = 30;

    private readonly Lock _gate = new();
    private readonly Queue<RecentFailure> _items = [];
    private readonly int _capacity;

    /// <summary>Creates an empty log.</summary>
    /// <param name="capacity">How many failures to keep. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public RecentFailureLog(int capacity = Capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Keeps <paramref name="failure"/>, dropping the oldest when the log is full.</summary>
    /// <param name="failure">The failure to keep. Scrub it first (<see cref="RecentFailure.Create"/> does).</param>
    public void Add(RecentFailure failure)
    {
        lock (_gate)
        {
            _items.Enqueue(failure);
            while (_items.Count > _capacity)
                _items.Dequeue();
        }
    }

    /// <summary>The kept failures, oldest first.</summary>
    /// <returns>A copy of the kept failures. Never null; empty when there are none.</returns>
    public IReadOnlyList<RecentFailure> Snapshot()
    {
        lock (_gate)
            return [.. _items];
    }

    /// <summary>How many failures are kept now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _items.Count;
        }
    }
}
