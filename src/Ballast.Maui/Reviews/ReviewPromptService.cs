// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

using Ballast.Maui.Dialogs;
using Ballast.Maui.Platform;
using Ballast.Maui.Storage;

namespace Ballast.Maui.Reviews;

/// <summary>The store's own rating prompt. The store decides whether it actually shows, so a launch is not a promise that a dialog appeared.</summary>
public interface IStoreReviewPrompt
{
    /// <summary>Launches the store's rating flow. Never throws.</summary>
    /// <returns>True when the flow was launched.</returns>
    Task<bool> RequestAsync();
}

/// <summary>Where there is no in-app rating prompt, nothing is launched.</summary>
public sealed class NoStoreReviewPrompt : IStoreReviewPrompt
{
    /// <inheritdoc />
    public Task<bool> RequestAsync() => Task.FromResult(false);
}

/// <summary>The keys and the timing of the review prompt, so an app keeps the names it already stores.</summary>
/// <param name="FirstRunKey">The stamped first run, in UTC ticks.</param>
/// <param name="FirstSessionKey">The process that stamped the first run.</param>
/// <param name="FinishedJourneysKey">The count of finished journeys.</param>
/// <param name="LastAskKey">When the prompt was last launched, in UTC ticks.</param>
/// <param name="AskCountKey">How many times the prompt was launched.</param>
/// <param name="SettleDelay">The wait after a journey ends, so the screen settles and the person sees the trip is over before a question appears.</param>
public sealed record ReviewPromptOptions(
    string FirstRunKey = "review.firstRunUtc",
    string FirstSessionKey = "review.firstSession",
    string FinishedJourneysKey = "review.finishedTrips",
    string LastAskKey = "review.lastAskUtc",
    string AskCountKey = "review.askCount",
    TimeSpan? SettleDelay = null)
{
    /// <summary>The default wait after a journey ends: three seconds.</summary>
    public static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromSeconds(3);

    /// <summary>The wait to use: <see cref="SettleDelay"/>, or three seconds when it is not set.</summary>
    public TimeSpan EffectiveSettleDelay => SettleDelay ?? DefaultSettleDelay;
}

/// <summary>
/// The store rating prompt, asked a few seconds after a journey ends, on a settled screen, and only when <see cref="ReviewPromptPolicy"/> says so.
/// The state lives in the install's answers. Nothing is asked on the first session: <see cref="Start"/> stamps the first run and that session once.
/// </summary>
public sealed class ReviewPromptService
{
    private readonly ReviewPromptOptions _options;
    private readonly IStoreReviewPrompt _prompt;
    private readonly IInstallChannel _channel;
    private readonly InstallAnswers _answers;
    private readonly Func<Task<bool>> _waitForSettledScreen;
    private readonly Func<Task<bool>> _onExcludedPage;

    /// <summary>Identifies this process, so the session that stamped the first run is known.</summary>
    private readonly string _thisSession = Guid.NewGuid().ToString("N");

    /// <summary>Creates the service.</summary>
    /// <param name="options">The keys and the settle delay.</param>
    /// <param name="prompt">The store's prompt.</param>
    /// <param name="channel">The install channel, which decides whether the store prompt applies.</param>
    /// <param name="answers">Where the state is kept.</param>
    /// <param name="waitForSettledScreen">Waits for a settled screen, and says whether one was reached.</param>
    /// <param name="onExcludedPage">Says, on the UI thread, whether a page that must stay free of prompts is showing.</param>
    public ReviewPromptService(
        ReviewPromptOptions options,
        IStoreReviewPrompt prompt,
        IInstallChannel channel,
        InstallAnswers answers,
        Func<Task<bool>> waitForSettledScreen,
        Func<Task<bool>> onExcludedPage)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(waitForSettledScreen);
        ArgumentNullException.ThrowIfNull(onExcludedPage);
        _options = options;
        _prompt = prompt;
        _channel = channel;
        _answers = answers;
        _waitForSettledScreen = waitForSettledScreen;
        _onExcludedPage = onExcludedPage;
    }

    /// <summary>Called at each start. Stamps the first run and the first session, once per install.</summary>
    public void Start()
    {
        if (_answers.Get(_options.FirstRunKey, 0L, 0L) == 0L)
            _answers.Set(_options.FirstRunKey, DateTime.UtcNow.Ticks);
        if (_answers.Get(_options.FirstSessionKey, string.Empty, string.Empty).Length == 0)
            _answers.Set(_options.FirstSessionKey, _thisSession);
    }

    /// <summary>When the store rating prompt was last launched, in UTC, or null. The survey reads it.</summary>
    /// <returns>The instant, or null.</returns>
    public DateTime? LastAskUtc()
    {
        var ticks = _answers.Get(_options.LastAskKey, 0L, 0L);
        return ticks == 0L ? null : new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// Records that a journey finished, and asks about the rating prompt once the screen has settled, if the policy allows it. Fire and forget:
    /// the ask never throws.
    /// </summary>
    public void JourneyFinished()
    {
        _answers.Set(_options.FinishedJourneysKey, _answers.Get(_options.FinishedJourneysKey, 0, 0) + 1);
        _ = AskWhenSettledAsync();
    }

    private ReviewPromptPolicy.State Load()
    {
        var firstRun = _answers.Get(_options.FirstRunKey, 0L, 0L);
        return new ReviewPromptPolicy.State(
            firstRun == 0L ? null : new DateTime(firstRun, DateTimeKind.Utc),
            _answers.Get(_options.FinishedJourneysKey, 0, 0),
            LastAskUtc(),
            _answers.Get(_options.AskCountKey, 0, 0));
    }

    private async Task AskWhenSettledAsync()
    {
        try
        {
            await Task.Delay(_options.EffectiveSettleDelay);
            if (!await _waitForSettledScreen())
                return;

            var onExcludedPage = await _onExcludedPage();
            var now = DateTime.UtcNow;
            var context = new ReviewPromptPolicy.Context(
                IsPlayInstall: _channel.Channel == InstallChannels.Play,
                IsFirstSession: _answers.Get(_options.FirstSessionKey, string.Empty, string.Empty) == _thisSession,
                OnExcludedPage: onExcludedPage,
                LastDialogUtc: DialogClock.LastUtc,
                NowUtc: now);

            if (ReviewPromptPolicy.FirstBlock(Load(), context) is { } reason)
            {
                Trace.WriteLine($"[ReviewPrompt] Not asked: {reason}.");
                return;
            }

            // Recorded before the flow is launched: an ask that the store then skips still counts against the limits.
            _answers.Set(_options.LastAskKey, now.Ticks);
            _answers.Set(_options.AskCountKey, _answers.Get(_options.AskCountKey, 0, 0) + 1);
            DialogClock.Mark();
            var launched = await _prompt.RequestAsync();
            Trace.WriteLine($"[ReviewPrompt] Asked (launched: {launched}).");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ReviewPrompt] Could not ask: {ex.Message}");
        }
    }
}
