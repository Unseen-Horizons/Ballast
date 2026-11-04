// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Ballast.Testing.Fakes;

/// <summary>
/// A logger provider that keeps every entry, with its category, its level, and the scopes that were open when it was written.
/// Add it to a host's logging to assert on what the app logged. Entries are kept in the order they were written.
/// </summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private static readonly AsyncLocal<ScopeNode?> Scope = new();

    private readonly object _lock = new();
    private readonly List<LogRecord> _records = [];

    /// <summary>Every entry written so far, in order. A copy: later entries do not appear in an earlier copy.</summary>
    public IReadOnlyList<LogRecord> Records
    {
        get
        {
            lock (_lock)
            {
                return [.. _records];
            }
        }
    }

    /// <summary>Creates a logger for one category. Every logger writes into this provider's list.</summary>
    /// <param name="categoryName">The category the logger reports under.</param>
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    /// <summary>Does nothing: the entries stay available after the host is disposed.</summary>
    public void Dispose()
    {
    }

    private void Add(LogRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
        }
    }

    private sealed record ScopeNode(object State, ScopeNode? Parent);

    /// <summary>One entry.</summary>
    /// <param name="Category">The logger's category.</param>
    /// <param name="Level">The entry's level.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The exception logged with the entry, or null.</param>
    /// <param name="Scopes">The scope states that were open, outermost first.</param>
    public sealed record LogRecord(string Category, LogLevel Level, string Message, Exception? Exception, IReadOnlyList<object> Scopes);

    private sealed class Logger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var previous = Scope.Value;
            Scope.Value = new ScopeNode(state, previous);
            return new Restore(previous);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            List<object> scopes = [];
            for (var node = Scope.Value; node is not null; node = node.Parent)
            {
                scopes.Insert(0, node.State);
            }

            owner.Add(new LogRecord(category, logLevel, formatter(state, exception), exception, scopes));
        }

        private sealed class Restore(ScopeNode? previous) : IDisposable
        {
            public void Dispose() => Scope.Value = previous;
        }
    }
}
