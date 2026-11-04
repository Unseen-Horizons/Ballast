// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Tests;

/// <summary>
/// The rules for offering a waiting crash report: offered once per run, sent only when the person says Send, either answer deletes it, and a failed send (or a closed question) keeps it for the next start.
/// </summary>
public sealed class CrashReportOfferTests : IDisposable
{
    private const string Report = "Crash: System.InvalidOperationException\nApp: 1.0 (1)\n\nSystem.InvalidOperationException: boom";

    private readonly TempRoot _root = new();
    private readonly CrashReportStore _store;
    private readonly Ui _ui = new();
    private readonly List<(string Text, string Subject)> _sent = [];
    private Func<CrashSendOutcome> _answer = () => CrashSendOutcome.Sent;

    public CrashReportOfferTests()
    {
        _store = new CrashReportStore(Path.Combine(_root.Path, "crash"));
    }

    public void Dispose() => _root.Dispose();

    private sealed class Ui : ICrashReportUi
    {
        public Queue<CrashPromptAnswer> Asks { get; } = [];

        public Queue<CrashPromptAnswer> Reads { get; } = [];

        public int AskCount { get; private set; }

        public List<string> TextsShown { get; } = [];

        public int SentShown { get; private set; }

        public int FailedShown { get; private set; }

        public Task<CrashPromptAnswer> AskAsync()
        {
            AskCount++;
            return Task.FromResult(Asks.Dequeue());
        }

        public Task<CrashPromptAnswer> ShowTextAsync(string reportText)
        {
            TextsShown.Add(reportText);
            return Task.FromResult(Reads.Dequeue());
        }

        public Task ShowSentAsync()
        {
            SentShown++;
            return Task.CompletedTask;
        }

        public Task ShowSendFailedAsync()
        {
            FailedShown++;
            return Task.CompletedTask;
        }
    }

    private CrashReportOffer NewOffer() => new(_store, _ui, (text, subject) =>
    {
        _sent.Add((text, subject));
        return Task.FromResult(_answer());
    });

    // ---- Nothing to offer -------------------------------------------------------------------

    [Fact]
    public async Task With_no_report_nothing_is_asked()
    {
        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.NothingPending, result);
        Assert.Equal(0, _ui.AskCount);
    }

    [Fact]
    public async Task A_file_with_nothing_in_it_is_deleted_without_asking()
    {
        _store.Save("   \n ");

        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.NothingPending, result);
        Assert.False(_store.HasPending);
        Assert.Equal(0, _ui.AskCount);
    }

    // ---- Send --------------------------------------------------------------------------------

    [Fact]
    public async Task Send_posts_the_exact_text_with_its_first_line_as_the_subject_then_deletes_the_file_and_says_thanks()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.Send);

        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.Sent, result);
        var (text, subject) = Assert.Single(_sent);
        Assert.Equal(Report, text);
        Assert.Equal("Crash: System.InvalidOperationException", subject);
        Assert.False(_store.HasPending);
        Assert.Equal(1, _ui.SentShown);
        Assert.Equal(0, _ui.FailedShown);
    }

    [Fact]
    public async Task A_failed_send_keeps_the_file_and_says_so()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.Send);
        _answer = () => CrashSendOutcome.Failed("FeedbackUnreachable");

        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.SendFailed, result);
        Assert.True(_store.HasPending);
        Assert.Equal(Report, _store.ReadPending());
        Assert.Equal(1, _ui.FailedShown);
        Assert.Equal(0, _ui.SentShown);
    }

    [Fact]
    public async Task A_failed_send_is_offered_again_at_the_next_start_but_not_again_in_the_same_run()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.Send);
        _answer = () => CrashSendOutcome.Failed("FeedbackUnreachable");
        var thisRun = NewOffer();

        await thisRun.RunAsync();
        var sameRunAgain = await thisRun.RunAsync();

        Assert.Equal(CrashOfferResult.AlreadyOffered, sameRunAgain);
        Assert.Equal(1, _ui.AskCount);

        // The next start: a new offer, the file still there, and this time it goes.
        _ui.Asks.Enqueue(CrashPromptAnswer.Send);
        _answer = () => CrashSendOutcome.Sent;
        var nextStart = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.Sent, nextStart);
        Assert.False(_store.HasPending);
    }

    [Fact]
    public async Task A_send_that_throws_leaves_the_file()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.Send);
        var offer = new CrashReportOffer(_store, _ui, (_, _) => throw new InvalidOperationException("the client blew up"));

        await Assert.ThrowsAsync<InvalidOperationException>(offer.RunAsync);

        Assert.True(_store.HasPending);
    }

    // ---- Don't send, and a closed question ---------------------------------------------------

    [Fact]
    public async Task Dont_send_deletes_the_file_and_sends_nothing()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.DontSend);

        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.Declined, result);
        Assert.Empty(_sent);
        Assert.False(_store.HasPending);
        Assert.Equal(0, _ui.SentShown + _ui.FailedShown);
    }

    [Fact]
    public async Task A_closed_question_is_not_an_answer_so_the_file_stays_and_nothing_is_sent()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.Dismissed);

        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.Deferred, result);
        Assert.Empty(_sent);
        Assert.True(_store.HasPending);
    }

    // ---- See what's sent ---------------------------------------------------------------------

    [Fact]
    public async Task See_whats_sent_shows_the_exact_text_and_then_asks_again()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.SeeFirst);
        _ui.Reads.Enqueue(CrashPromptAnswer.Send);

        var result = await NewOffer().RunAsync();

        Assert.Equal(Report, Assert.Single(_ui.TextsShown));
        Assert.Equal(CrashOfferResult.Sent, result);
        Assert.Equal(Report, Assert.Single(_sent).Text);
    }

    [Fact]
    public async Task After_reading_it_the_person_can_decline_and_nothing_is_sent()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.SeeFirst);
        _ui.Reads.Enqueue(CrashPromptAnswer.DontSend);

        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.Declined, result);
        Assert.Empty(_sent);
        Assert.False(_store.HasPending);
    }

    [Fact]
    public async Task Closing_the_text_without_choosing_keeps_the_file()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.SeeFirst);
        _ui.Reads.Enqueue(CrashPromptAnswer.Dismissed);

        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.Deferred, result);
        Assert.True(_store.HasPending);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task A_screen_that_keeps_saying_see_first_cannot_trap_the_offer()
    {
        _store.Save(Report);
        _ui.Asks.Enqueue(CrashPromptAnswer.SeeFirst);
        for (var i = 0; i < 10; i++)
            _ui.Reads.Enqueue(CrashPromptAnswer.SeeFirst);

        var result = await NewOffer().RunAsync();

        Assert.Equal(CrashOfferResult.Deferred, result);
        Assert.Empty(_sent);
        Assert.True(_store.HasPending);
    }

    // ---- Size --------------------------------------------------------------------------------

    [Fact]
    public async Task A_file_longer_than_the_feedback_limit_is_cut_before_it_is_shown_or_sent()
    {
        _store.Save("Crash: x\n" + new string('y', 10_000));
        _ui.Asks.Enqueue(CrashPromptAnswer.Send);

        await NewOffer().RunAsync();

        Assert.True(Assert.Single(_sent).Text.Length <= CrashReportText.MaxLength);
    }
}
