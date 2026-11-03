// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Reviews;

/// <summary>Why the store rating prompt is not asked now. The first one that holds is the reason; null means ask.</summary>
public enum ReviewPromptBlock
{
    /// <summary>Not installed from the store that runs the prompt: the store prompt is that store's only.</summary>
    NotPlayInstall,

    /// <summary>The first session of this install: the person has not used the app long enough to be asked.</summary>
    FirstSession,

    /// <summary>The first run is not stamped yet, or fewer than 14 days have passed since it.</summary>
    TooSoonSinceFirstRun,

    /// <summary>Fewer than three journeys have finished.</summary>
    TooFewFinishedJourneys,

    /// <summary>A page the app keeps free of prompts is showing.</summary>
    OnExcludedPage,

    /// <summary>A crash offer, the permission flow or another dialog was shown within the last five minutes.</summary>
    DialogTooRecent,

    /// <summary>Fewer than 90 days have passed since the last ask.</summary>
    AskedRecently,

    /// <summary>Three asks have already been made on this install.</summary>
    AskLimitReached,
}

/// <summary>
/// When the store rating prompt may be asked. Pure, so every condition is tested without a device. The app asks a few seconds after a journey ends,
/// on a settled screen, and only when this says yes.
/// </summary>
public static class ReviewPromptPolicy
{
    /// <summary>The journeys that must have finished before the first ask.</summary>
    public const int MinimumFinishedJourneys = 3;

    /// <summary>The most asks made on one install.</summary>
    public const int MaximumAsks = 3;

    /// <summary>The span after the first run before the first ask.</summary>
    public static readonly TimeSpan MinimumSinceFirstRun = TimeSpan.FromDays(14);

    /// <summary>The span between two asks.</summary>
    public static readonly TimeSpan MinimumSinceLastAsk = TimeSpan.FromDays(90);

    /// <summary>The span after another dialog before the rating prompt may appear.</summary>
    public static readonly TimeSpan DialogGap = TimeSpan.FromMinutes(5);

    /// <summary>What is stored for this install.</summary>
    /// <param name="FirstRunUtc">Stamped once, at the first start, in UTC. Null before it is stamped.</param>
    /// <param name="FinishedJourneys">Journeys that ended as finished.</param>
    /// <param name="LastAskUtc">When the prompt was last launched, in UTC, or null.</param>
    /// <param name="AskCount">How many times the prompt has been launched on this install.</param>
    public sealed record State(DateTime? FirstRunUtc, int FinishedJourneys, DateTime? LastAskUtc, int AskCount);

    /// <summary>What is known at the moment of asking.</summary>
    /// <param name="IsPlayInstall">Installed from the store that runs the prompt.</param>
    /// <param name="IsFirstSession">This process is the one that stamped the first run.</param>
    /// <param name="OnExcludedPage">The page on screen is one the app keeps free of prompts.</param>
    /// <param name="LastDialogUtc">When another dialog last showed, in UTC, or null.</param>
    /// <param name="NowUtc">The current instant, in UTC.</param>
    public sealed record Context(bool IsPlayInstall, bool IsFirstSession, bool OnExcludedPage, DateTime? LastDialogUtc, DateTime NowUtc);

    /// <summary>The first condition that blocks the prompt, or null when it may be asked.</summary>
    /// <param name="state">What is stored.</param>
    /// <param name="context">What is known now.</param>
    /// <returns>The first block that holds, or null.</returns>
    public static ReviewPromptBlock? FirstBlock(State state, Context context)
    {
        if (!context.IsPlayInstall)
            return ReviewPromptBlock.NotPlayInstall;
        if (context.IsFirstSession)
            return ReviewPromptBlock.FirstSession;
        if (state.FirstRunUtc is not { } firstRun || context.NowUtc - firstRun < MinimumSinceFirstRun)
            return ReviewPromptBlock.TooSoonSinceFirstRun;
        if (state.FinishedJourneys < MinimumFinishedJourneys)
            return ReviewPromptBlock.TooFewFinishedJourneys;
        if (context.OnExcludedPage)
            return ReviewPromptBlock.OnExcludedPage;
        if (context.LastDialogUtc is { } dialog && context.NowUtc - dialog < DialogGap)
            return ReviewPromptBlock.DialogTooRecent;
        if (state.LastAskUtc is { } lastAsk && context.NowUtc - lastAsk < MinimumSinceLastAsk)
            return ReviewPromptBlock.AskedRecently;
        if (state.AskCount >= MaximumAsks)
            return ReviewPromptBlock.AskLimitReached;
        return null;
    }

    /// <summary>Whether the prompt may be asked now.</summary>
    /// <param name="state">What is stored.</param>
    /// <param name="context">What is known now.</param>
    /// <returns>True when no condition blocks it.</returns>
    public static bool ShouldAsk(State state, Context context) => FirstBlock(state, context) is null;
}
