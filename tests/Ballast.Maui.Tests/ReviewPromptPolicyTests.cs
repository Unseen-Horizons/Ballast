// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Reviews;

namespace Ballast.Maui.Tests;

/// <summary>
/// The store rating prompt's rules, one condition at a time: a ready install is asked, and each condition that is not met blocks with its own reason, in the order <see cref="ReviewPromptPolicy.FirstBlock"/> checks them.
/// </summary>
public sealed class ReviewPromptPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static ReviewPromptPolicy.State ReadyState() => new(
        FirstRunUtc: Now - TimeSpan.FromDays(20),
        FinishedJourneys: 3,
        LastAskUtc: null,
        AskCount: 0);

    private static ReviewPromptPolicy.Context ReadyContext() => new(
        IsPlayInstall: true,
        IsFirstSession: false,
        OnExcludedPage: false,
        LastDialogUtc: null,
        NowUtc: Now);

    [Fact]
    public void A_ready_install_is_asked()
    {
        Assert.Null(ReviewPromptPolicy.FirstBlock(ReadyState(), ReadyContext()));
        Assert.True(ReviewPromptPolicy.ShouldAsk(ReadyState(), ReadyContext()));
    }

    [Fact]
    public void An_install_from_outside_play_is_never_asked()
    {
        var context = ReadyContext() with { IsPlayInstall = false };

        Assert.Equal(ReviewPromptBlock.NotPlayInstall, ReviewPromptPolicy.FirstBlock(ReadyState(), context));
    }

    [Fact]
    public void The_first_session_is_never_asked()
    {
        var context = ReadyContext() with { IsFirstSession = true };

        Assert.Equal(ReviewPromptBlock.FirstSession, ReviewPromptPolicy.FirstBlock(ReadyState(), context));
    }

    [Fact]
    public void A_first_run_that_was_never_stamped_blocks()
    {
        var state = ReadyState() with { FirstRunUtc = null };

        Assert.Equal(ReviewPromptBlock.TooSoonSinceFirstRun, ReviewPromptPolicy.FirstBlock(state, ReadyContext()));
    }

    [Fact]
    public void Fourteen_days_since_the_first_run_is_the_least_that_passes()
    {
        var justUnder = ReadyState() with { FirstRunUtc = Now - TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1) };
        var exactly = ReadyState() with { FirstRunUtc = Now - TimeSpan.FromDays(14) };

        Assert.Equal(ReviewPromptBlock.TooSoonSinceFirstRun, ReviewPromptPolicy.FirstBlock(justUnder, ReadyContext()));
        Assert.Null(ReviewPromptPolicy.FirstBlock(exactly, ReadyContext()));
    }

    [Fact]
    public void Fewer_than_three_finished_journeys_block()
    {
        var two = ReadyState() with { FinishedJourneys = 2 };

        Assert.Equal(ReviewPromptBlock.TooFewFinishedJourneys, ReviewPromptPolicy.FirstBlock(two, ReadyContext()));
        Assert.Null(ReviewPromptPolicy.FirstBlock(ReadyState() with { FinishedJourneys = 3 }, ReadyContext()));
    }

    [Fact]
    public void A_page_that_must_stay_free_of_prompts_blocks()
    {
        // The app decides which page is on screen; the policy takes that answer as a flag.
        var context = ReadyContext() with { OnExcludedPage = true };

        Assert.Equal(ReviewPromptBlock.OnExcludedPage, ReviewPromptPolicy.FirstBlock(ReadyState(), context));
    }

    [Fact]
    public void A_dialog_shown_under_five_minutes_ago_blocks()
    {
        var context = ReadyContext() with { LastDialogUtc = Now - TimeSpan.FromMinutes(4) };

        Assert.Equal(ReviewPromptBlock.DialogTooRecent, ReviewPromptPolicy.FirstBlock(ReadyState(), context));
    }

    [Fact]
    public void A_dialog_five_minutes_ago_does_not_block()
    {
        var context = ReadyContext() with { LastDialogUtc = Now - TimeSpan.FromMinutes(5) };

        Assert.Null(ReviewPromptPolicy.FirstBlock(ReadyState(), context));
    }

    [Fact]
    public void An_ask_less_than_ninety_days_ago_blocks()
    {
        var state = ReadyState() with { LastAskUtc = Now - TimeSpan.FromDays(89) };

        Assert.Equal(ReviewPromptBlock.AskedRecently, ReviewPromptPolicy.FirstBlock(state, ReadyContext()));
    }

    [Fact]
    public void An_ask_ninety_days_ago_does_not_block()
    {
        var state = ReadyState() with { LastAskUtc = Now - TimeSpan.FromDays(90) };

        Assert.Null(ReviewPromptPolicy.FirstBlock(state, ReadyContext()));
    }

    [Fact]
    public void Three_asks_on_one_install_is_the_limit()
    {
        Assert.Null(ReviewPromptPolicy.FirstBlock(ReadyState() with { AskCount = 2 }, ReadyContext()));
        Assert.Equal(ReviewPromptBlock.AskLimitReached, ReviewPromptPolicy.FirstBlock(ReadyState() with { AskCount = 3 }, ReadyContext()));
    }

    [Fact]
    public void The_first_failing_condition_is_the_reason()
    {
        var state = ReadyState() with { FinishedJourneys = 0, AskCount = 3 };
        var context = ReadyContext() with { IsPlayInstall = false };

        Assert.Equal(ReviewPromptBlock.NotPlayInstall, ReviewPromptPolicy.FirstBlock(state, context));
    }
}
