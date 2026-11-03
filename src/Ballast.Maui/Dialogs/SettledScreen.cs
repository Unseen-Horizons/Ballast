// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Dialogs;

/// <summary>
/// Waits for a settled screen: the app's page is loaded and nothing (a first-start flow, a modal) sits over it. The start-up prompts and the
/// store rating prompt use it, so each asks over a screen the person is actually looking at.
/// </summary>
/// <remarks>
/// The check runs on the UI thread through the function it is given. The platform implementation asks the navigation shell (MAUI's
/// <c>MauiSettledScreen</c> does).
/// </remarks>
public sealed class SettledScreen
{
    /// <summary>The default number of checks before giving up: 60, about 30 seconds at the default pause.</summary>
    public const int DefaultAttempts = 60;

    /// <summary>The default pause between two checks.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromMilliseconds(500);

    private readonly Func<Task<bool>> _isSettledOnUiThread;
    private readonly int _attempts;
    private readonly TimeSpan _pause;

    /// <summary>Creates the wait.</summary>
    /// <param name="isSettledOnUiThread">Says whether the screen is settled now. Called on the UI thread.</param>
    /// <param name="attempts">How many checks are made before giving up. At least 1.</param>
    /// <param name="pause">The pause between two checks. Zero or more.</param>
    public SettledScreen(Func<Task<bool>> isSettledOnUiThread, int attempts = DefaultAttempts, TimeSpan? pause = null)
    {
        ArgumentNullException.ThrowIfNull(isSettledOnUiThread);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var span = pause ?? DefaultPause;
        if (span < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pause), span, "The pause must not be negative.");
        _isSettledOnUiThread = isSettledOnUiThread;
        _attempts = attempts;
        _pause = span;
    }

    /// <summary>
    /// Checks until the screen is settled or the attempts run out.
    /// </summary>
    /// <returns>True when a settled screen was reached; false when none was within the attempts.</returns>
    public async Task<bool> WaitAsync()
    {
        for (var attempt = 0; attempt < _attempts; attempt++)
        {
            if (await _isSettledOnUiThread())
                return true;

            if (attempt < _attempts - 1)
                await Task.Delay(_pause);
        }

        return false;
    }
}
