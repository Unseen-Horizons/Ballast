// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Ballast.AspNetCore.Tests.Support;

/// <summary>A logger provider that keeps every entry, with the scopes that were open when it was written.</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private static readonly AsyncLocal<ScopeNode?> _scope = new();

    private readonly object _lock = new();
    private readonly List<LogRecord> _records = [];

    /// <summary>Every entry written so far, in order.</summary>
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

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

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
    internal sealed record LogRecord(string Category, LogLevel Level, string Message, Exception? Exception, IReadOnlyList<object> Scopes);

    private sealed class Logger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var previous = _scope.Value;
            _scope.Value = new ScopeNode(state, previous);
            return new Restore(previous);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            List<object> scopes = [];
            for (var node = _scope.Value; node is not null; node = node.Parent)
            {
                scopes.Insert(0, node.State);
            }

            owner.Add(new LogRecord(category, logLevel, formatter(state, exception), exception, scopes));
        }

        private sealed class Restore(ScopeNode? previous) : IDisposable
        {
            public void Dispose() => _scope.Value = previous;
        }
    }
}
