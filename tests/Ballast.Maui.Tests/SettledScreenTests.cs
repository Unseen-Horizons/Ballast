// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Dialogs;

namespace Ballast.Maui.Tests;

/// <summary>The wait for a settled screen: it stops at the first settled check, and gives up after its attempts.</summary>
public sealed class SettledScreenTests
{
    [Fact]
    public async Task A_settled_screen_at_the_first_check_is_reached_at_once()
    {
        var calls = 0;
        var wait = new SettledScreen(() => { calls++; return Task.FromResult(true); }, attempts: 3, pause: TimeSpan.Zero);

        Assert.True(await wait.WaitAsync());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_screen_that_settles_on_a_later_check_is_reached_then()
    {
        var answers = new Queue<bool>([false, false, true]);
        var calls = 0;
        var wait = new SettledScreen(() => { calls++; return Task.FromResult(answers.Dequeue()); }, attempts: 5, pause: TimeSpan.Zero);

        Assert.True(await wait.WaitAsync());
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task A_screen_that_never_settles_gives_up_after_its_attempts()
    {
        var calls = 0;
        var wait = new SettledScreen(() => { calls++; return Task.FromResult(false); }, attempts: 4, pause: TimeSpan.Zero);

        Assert.False(await wait.WaitAsync());
        Assert.Equal(4, calls);
    }

    [Fact]
    public void The_defaults_are_sixty_attempts_half_a_second_apart()
    {
        Assert.Equal(60, SettledScreen.DefaultAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(500), SettledScreen.DefaultPause);
    }

    [Fact]
    public void No_attempts_and_a_negative_pause_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SettledScreen(() => Task.FromResult(true), attempts: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SettledScreen(() => Task.FromResult(true), pause: TimeSpan.FromMilliseconds(-1)));
    }
}
