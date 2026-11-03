// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

using Ballast.Maui.Platform;

namespace Ballast.Maui.Diagnostics;

/// <summary>
/// Catches the crashes the app can see and writes one local report for the person to send later, or not.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Install"/> hooks <see cref="AppDomain.UnhandledException"/>, and <see cref="TaskScheduler.UnobservedTaskException"/>. Each
/// unhandled exception writes one file (<see cref="CrashReportStore"/>): the exception, the build, the operating system, the time and the recent
/// handled failures, scrubbed and cut to fit the feedback message (<see cref="CrashReportText"/>). A newer crash replaces an older unsent one, and
/// the same exception arriving through two hooks is written once. At the next start the app asks the person whether to send it. Nothing is sent
/// from here. The Android runtime's own hook is attached by the platform code (see <c>AndroidCrashHooks</c>).
/// </para>
/// <para>
/// An unobserved task exception does not write a report: a task nobody awaited does not close the app, so "the app closed unexpectedly" would be
/// untrue. It is recorded as a handled failure instead, which rides along on a later report.
/// </para>
/// <para>
/// What this cannot see: native crashes and freezes are not .NET exceptions. The platform's own vitals report them.
/// </para>
/// </remarks>
public sealed class CrashReporter
{
    private static readonly Lock InstallGate = new();
    private static CrashReporter? _active;

    private readonly CrashReportStore _store;
    private readonly IAppBuildInfo _info;
    private readonly ClientLog _log;
    private readonly Lock _writeGate = new();
    private Exception? _lastWritten;

    /// <summary>Creates a reporter. It writes nothing until <see cref="Install"/> is called, or <see cref="Record"/> is called by hand.</summary>
    /// <param name="store">Where the waiting report is kept.</param>
    /// <param name="info">The build and the platform, read when a report is written.</param>
    /// <param name="log">The failures kept for a report, and the trace.</param>
    public CrashReporter(CrashReportStore store, IAppBuildInfo info, ClientLog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _info = info;
        _log = log;
    }

    /// <summary>The store the waiting report is written to.</summary>
    public CrashReportStore Store => _store;

    /// <summary>
    /// Hooks the crash events of the process. Only the first reporter to install is used: a second call on any reporter does nothing, so each
    /// event writes one report.
    /// </summary>
    public void Install()
    {
        lock (InstallGate)
        {
            if (_active is not null)
                return;

            _active = this;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
    }

    /// <summary>
    /// Writes the report for one exception. It runs inside a crash, so it never throws: a failure here is traced, and the report is left
    /// as it was.
    /// </summary>
    /// <param name="exception">What was thrown, or null when there is none.</param>
    /// <param name="source">Which hook caught it, for the report.</param>
    /// <returns>True when a report for this exception is on disk; false when it could not be written.</returns>
    public bool Record(Exception? exception, string source)
    {
        try
        {
            lock (_writeGate)
            {
                // The same exception can arrive through two hooks; one file is enough.
                if (exception is not null && ReferenceEquals(exception, _lastWritten))
                    return true;

                _lastWritten = exception;
                var input = new CrashReportInput(
                    DateTime.UtcNow,
                    Read(() => _info.VersionString),
                    Read(() => _info.BuildString),
                    Read(() => _info.PlatformName),
                    Read(() => _info.OsVersion),
                    source,
                    exception,
                    _log.Recent.Snapshot());
                return _store.Save(CrashReportText.Compose(input));
            }
        }
        catch (Exception inner)
        {
            // Nothing more can be done from inside a crash; the trace is all there is.
            Trace.WriteLine($"[CrashReporter] Could not write the crash report: {inner}");
            return false;
        }
    }

    private void OnUnhandled(object? sender, UnhandledExceptionEventArgs e) =>
        Record(e.ExceptionObject as Exception, "AppDomain.UnhandledException");

    private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) =>
        _log.Failure("UnobservedTask", "A task failed and nobody awaited it", ex: e.Exception);

    private static string Read(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[CrashReporter] Could not read a platform value for the report: {ex.Message}");
            return "unknown";
        }
    }
}
