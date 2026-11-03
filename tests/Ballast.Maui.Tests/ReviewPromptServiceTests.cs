// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Platform;
using Ballast.Maui.Reviews;
using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>
/// The review prompt's state and its ask: the first run and the first session are stamped once, journeys are counted, and an ask happens only when the policy allows it.
/// Only one test here reaches the ask itself, because the dialog clock is process-wide and a second ask within five minutes is correctly refused.
/// </summary>
public sealed class ReviewPromptServiceTests
{
    private readonly FakePreferenceStore _preferences = new();
    private readonly RecordingLog _log = new();
    private readonly InstallAnswers _answers;
    private readonly FakeStoreReviewPrompt _prompt = new();

    public ReviewPromptServiceTests()
    {
        _answers = new InstallAnswers(_preferences, "test.install", _log);
    }

    private ReviewPromptService Create(
        string channel = InstallChannels.Play,
        bool settled = true,
        bool excludedPage = false) =>
        new(
            new ReviewPromptOptions(SettleDelay: TimeSpan.Zero),
            _prompt,
            new FixedInstallChannel(channel),
            _answers,
            () => Task.FromResult(settled),
            () => Task.FromResult(excludedPage));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10);
    }

    private void SeedReadyInstall()
    {
        var options = new ReviewPromptOptions();
        _answers.Set(options.FirstRunKey, (DateTime.UtcNow - TimeSpan.FromDays(20)).Ticks);
        _answers.Set(options.FinishedJourneysKey, 3);
    }

    [Fact]
    public void Start_stamps_the_first_run_once()
    {
        var service = Create();

        service.Start();
        var firstRun = _answers.Get(new ReviewPromptOptions().FirstRunKey, 0L, 0L);
        service.Start();

        Assert.NotEqual(0L, firstRun);
        Assert.Equal(firstRun, _answers.Get(new ReviewPromptOptions().FirstRunKey, 0L, 0L));
        Assert.NotEqual(string.Empty, _answers.Get(new ReviewPromptOptions().FirstSessionKey, string.Empty, string.Empty));
    }

    [Fact]
    public void Each_finished_journey_is_counted()
    {
        var service = Create(settled: false);

        service.JourneyFinished();
        service.JourneyFinished();
        service.JourneyFinished();

        Assert.Equal(3, _answers.Get(new ReviewPromptOptions().FinishedJourneysKey, 0, 0));
    }

    [Fact]
    public void No_ask_has_happened_before_the_first_one()
    {
        Assert.Null(Create().LastAskUtc());
    }

    [Fact]
    public async Task A_ready_install_is_asked_once_after_the_journey_and_the_ask_is_recorded()
    {
        SeedReadyInstall();

        Create().JourneyFinished();
        await WaitUntilAsync(() => _prompt.Asks > 0);
        await Task.Delay(50);

        Assert.Equal(1, _prompt.Asks);
        Assert.Equal(1, _answers.Get(new ReviewPromptOptions().AskCountKey, 0, 0));
        Assert.NotNull(Create().LastAskUtc());
    }

    [Fact]
    public async Task The_first_session_is_not_asked_even_when_the_install_is_ready()
    {
        SeedReadyInstall();
        var service = Create();
        service.Start();

        service.JourneyFinished();
        await Task.Delay(100);

        Assert.Equal(0, _prompt.Asks);
    }

    [Fact]
    public async Task A_install_from_outside_play_is_not_asked()
    {
        SeedReadyInstall();

        Create(channel: InstallChannels.Direct).JourneyFinished();
        await Task.Delay(100);

        Assert.Equal(0, _prompt.Asks);
    }

    [Fact]
    public async Task A_page_that_must_stay_free_of_prompts_blocks_the_ask()
    {
        SeedReadyInstall();

        Create(excludedPage: true).JourneyFinished();
        await Task.Delay(100);

        Assert.Equal(0, _prompt.Asks);
    }

    [Fact]
    public async Task No_ask_when_the_screen_never_settles()
    {
        SeedReadyInstall();

        Create(settled: false).JourneyFinished();
        await Task.Delay(100);

        Assert.Equal(0, _prompt.Asks);
    }

    [Fact]
    public void The_settle_delay_defaults_to_three_seconds_and_can_be_set()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), new ReviewPromptOptions().EffectiveSettleDelay);
        Assert.Equal(TimeSpan.FromSeconds(1), new ReviewPromptOptions(SettleDelay: TimeSpan.FromSeconds(1)).EffectiveSettleDelay);
    }

    [Fact]
    public async Task No_store_prompt_launches_nothing()
    {
        Assert.False(await new NoStoreReviewPrompt().RequestAsync());
    }
}
