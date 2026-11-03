// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Diagnostics;

/// <summary>
/// Where a client-side warning and a handled failure go. The app's own log facade forwards to one of these, so every
/// library component writes its trace and its crash-report entries the same way.
/// </summary>
public interface IClientLog
{
    /// <summary>
    /// Records a handled failure: one trace line with everything, and one entry kept for a crash report. The entry never
    /// carries the detail or the exception's message, only the source, the summary, the code, the trace id and the
    /// exception's type name.
    /// </summary>
    /// <param name="source">Where it happened, usually the calling type's name.</param>
    /// <param name="summary">What was being attempted, in developer terms. Never shown to a user.</param>
    /// <param name="code">A stable error code, when the failure has one.</param>
    /// <param name="detail">More detail for the trace line only. Never put personal values in it.</param>
    /// <param name="traceId">The server's trace id for the failing request, when there is one.</param>
    /// <param name="ex">The exception, when there is one.</param>
    void Failure(string source, string summary, string? code = null, string? detail = null, string? traceId = null, Exception? ex = null);

    /// <summary>
    /// Records an event that is not a failure but that the data no longer carries in full. A trace line only. Never put
    /// coordinates or other personal values in <paramref name="summary"/>.
    /// </summary>
    /// <param name="source">Where it happened.</param>
    /// <param name="summary">What happened, in developer terms.</param>
    void Warning(string source, string summary);
}
