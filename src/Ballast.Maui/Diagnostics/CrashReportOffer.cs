// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace Ballast.Maui.Diagnostics;

/// <summary>What the person chose when asked about a waiting crash report.</summary>
public enum CrashPromptAnswer
{
    /// <summary>Send it.</summary>
    Send,

    /// <summary>Do not send it, and do not ask about this one again.</summary>
    DontSend,

    /// <summary>Show exactly what would be sent, then ask again.</summary>
    SeeFirst,

    /// <summary>Closed the question without answering it (back button, a tap outside). Not an answer.</summary>
    Dismissed,
}

/// <summary>How an offer ended.</summary>
public enum CrashOfferResult
{
    /// <summary>No report was waiting.</summary>
    NothingPending,

    /// <summary>A report was already offered during this run of the app, so it was not offered again.</summary>
    AlreadyOffered,

    /// <summary>The person sent it, the server took it, and the file is gone.</summary>
    Sent,

    /// <summary>The person chose not to send it, and the file is gone.</summary>
    Declined,

    /// <summary>The person chose to send it but the send failed. The file is kept and offered again at the next start.</summary>
    SendFailed,

    /// <summary>The question was closed without an answer. The file is kept and offered again at the next start.</summary>
    Deferred,
}

/// <summary>The result of one send of a crash report.</summary>
/// <param name="Success">Whether the server took the report.</param>
/// <param name="Code">A stable code for a failure, or null.</param>
public sealed record CrashSendOutcome(bool Success, string? Code)
{
    /// <summary>The report was taken.</summary>
    public static CrashSendOutcome Sent { get; } = new(true, null);

    /// <summary>A failed send with its code.</summary>
    /// <param name="code">The stable code of the failure.</param>
    /// <returns>The outcome.</returns>
    public static CrashSendOutcome Failed(string code) => new(false, code);
}

/// <summary>The screens a crash offer shows. The platform implementation is <c>MauiCrashReportUi</c>.</summary>
public interface ICrashReportUi
{
    /// <summary>The question, with Send, Don't send and See what's sent.</summary>
    /// <returns>The answer, or <see cref="CrashPromptAnswer.Dismissed"/> when the question was closed another way.</returns>
    Task<CrashPromptAnswer> AskAsync();

    /// <summary>
    /// Shows exactly <paramref name="reportText"/>, the text that would be sent, and asks again with Send and Don't send.
    /// <see cref="CrashPromptAnswer.SeeFirst"/> is not offered here.
    /// </summary>
    /// <param name="reportText">The text that would be sent.</param>
    /// <returns>Send or Don't send, or <see cref="CrashPromptAnswer.Dismissed"/>.</returns>
    Task<CrashPromptAnswer> ShowTextAsync(string reportText);

    /// <summary>The report went. Says thank you.</summary>
    Task ShowSentAsync();

    /// <summary>The send failed: says so, and that the report was kept and will be offered again.</summary>
    Task ShowSendFailedAsync();
}

/// <summary>
/// The rules for offering a waiting crash report, free of platform types so each is pinned by a test:
/// <list type="bullet">
/// <item>it is offered at most once per run of the app;</item>
/// <item>nothing is sent unless the person chooses Send, and "See what's sent" shows the exact text first;</item>
/// <item>either answer (sent, or Don't send) deletes the file, and a closed question is not an answer, so the file stays;</item>
/// <item>a failed send keeps the file, says so, and the report is offered again at the next start.</item>
/// </list>
/// What is sent is the send delegate's business: the app's feedback service, with its own category and no email. It is given the report text and
/// its subject.
/// </summary>
public sealed class CrashReportOffer
{
    private readonly CrashReportStore _store;
    private readonly ICrashReportUi _ui;
    private readonly Func<string, string, Task<CrashSendOutcome>> _send;
    private int _offered;

    /// <summary>Creates the offer.</summary>
    /// <param name="store">The waiting report.</param>
    /// <param name="ui">The screens.</param>
    /// <param name="send">Sends the report text under its subject, and says whether it was taken.</param>
    public CrashReportOffer(CrashReportStore store, ICrashReportUi ui, Func<string, string, Task<CrashSendOutcome>> send)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(send);
        _store = store;
        _ui = ui;
        _send = send;
    }

    /// <summary>Asks about the waiting report, once per run, and acts on the answer.</summary>
    /// <returns>How the offer ended.</returns>
    public async Task<CrashOfferResult> RunAsync()
    {
        if (Volatile.Read(ref _offered) == 1)
            return CrashOfferResult.AlreadyOffered;

        var text = _store.ReadPending();
        if (text is null)
            return CrashOfferResult.NothingPending;

        if (string.IsNullOrWhiteSpace(text))
        {
            // A file with nothing in it is not a report: nothing to ask about, and nothing worth keeping.
            _store.Delete();
            return CrashOfferResult.NothingPending;
        }

        if (Interlocked.Exchange(ref _offered, 1) == 1)
            return CrashOfferResult.AlreadyOffered;

        if (text.Length > CrashReportText.MaxLength)
            text = CrashReportText.Clip(text, CrashReportText.MaxLength);

        // Reading is bounded: a screen that kept answering "See what's sent" must not trap the offer in a loop.
        var answer = await _ui.AskAsync();
        for (var looks = 0; looks < 3 && answer == CrashPromptAnswer.SeeFirst; looks++)
            answer = await _ui.ShowTextAsync(text);

        switch (answer)
        {
            case CrashPromptAnswer.DontSend:
                if (!_store.Delete())
                    Trace.WriteLine("[CrashReportOffer] The report was declined but its file could not be removed; it will be offered again.");
                return CrashOfferResult.Declined;

            case CrashPromptAnswer.Send:
                var result = await _send(text, CrashReportText.SubjectFor(text));
                if (!result.Success)
                {
                    Trace.WriteLine($"[CrashReportOffer] Sending the crash report failed ({result.Code}); keeping it for the next start.");
                    await _ui.ShowSendFailedAsync();
                    return CrashOfferResult.SendFailed;
                }

                if (!_store.Delete())
                    Trace.WriteLine("[CrashReportOffer] The report was sent but its file could not be removed; it may be offered again.");
                await _ui.ShowSentAsync();
                return CrashOfferResult.Sent;

            default:
                return CrashOfferResult.Deferred;
        }
    }
}
