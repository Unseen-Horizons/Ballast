// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Testing.Fakes;

/// <summary>
/// Sets a process environment variable for the length of a scope and puts back what was there when the scope ends. Scopes nest:
/// dispose them in the reverse order they were made. Environment variables are process-wide, so a test that uses one must not run
/// beside another that reads the same name; xUnit runs tests of one class one at a time by default.
/// </summary>
public sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;
    private bool _disposed;

    private EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>Sets the variable now. Dispose the result to restore the previous value.</summary>
    /// <param name="name">The variable's name. Required, not blank.</param>
    /// <param name="value">The value to set, or null to remove the variable for the scope.</param>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public static EnvironmentVariableScope Set(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new EnvironmentVariableScope(name, value);
    }

    /// <summary>Puts the previous value back, or removes the variable when it was not set before. Disposing twice does nothing more.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Environment.SetEnvironmentVariable(_name, _previous);
    }
}
