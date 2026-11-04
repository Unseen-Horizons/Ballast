// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

using Ballast.Maui.Dialogs;

namespace Ballast.Maui.Diagnostics;

/// <summary>
/// At start, if a crash report is waiting, waits until the person is looking at a settled screen and asks whether to send it.
/// <see cref="CrashReportOffer"/> holds the rules. Nothing is sent without a Send answer.
/// </summary>
/// <remarks>
/// The report is sent with the category the app's feedback service takes for crash reports, and with no email, so a report is never tied to an
/// account's address. The category is the app's to name; <see cref="DefaultCategory"/> is what the apps use.
/// </remarks>
public sealed class CrashReportStartup
{
    /// <summary>The feedback category the apps send crash reports under.</summary>
    public const string DefaultCategory = "Crash";

    private readonly CrashReportStore _store;
    private readonly ICrashReportUi _ui;
    private readonly Func<string, string, Task<CrashSendOutcome>> _send;
    private readonly Func<Task<bool>> _waitForSettledScreen;
    private CrashReportOffer? _offer;

    /// <summary>Creates the start-up offer.</summary>
    /// <param name="store">The waiting report.</param>
    /// <param name="ui">The screens.</param>
    /// <param name="send">Sends the report text under its subject, with the app's crash category and no email.</param>
    /// <param name="waitForSettledScreen">Waits until a settled screen is up, and says whether one was reached.</param>
    public CrashReportStartup(
        CrashReportStore store,
        ICrashReportUi ui,
        Func<string, string, Task<CrashSendOutcome>> send,
        Func<Task<bool>> waitForSettledScreen)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(waitForSettledScreen);
        _store = store;
        _ui = ui;
        _send = send;
        _waitForSettledScreen = waitForSettledScreen;
    }

    /// <summary>
    /// Offers the waiting report, if any. Meant for fire and forget: it never throws. The offer is made once per process, however often this is called.
    /// </summary>
    /// <returns>A task that completes when the offer has ended, or was not needed.</returns>
    public async Task TryOfferAsync()
    {
        try
        {
            if (!_store.HasPending)
                return;

            if (!await _waitForSettledScreen())
            {
                // The report stays on disk, so the next start asks. Nothing is lost by not asking over a screen that never settled.
                Trace.WriteLine("[CrashReportStartup] No settled screen to ask over; the crash report is kept for the next start.");
                return;
            }

            DialogClock.Mark();
            _offer ??= new CrashReportOffer(_store, _ui, _send);
            await _offer.RunAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[CrashReportStartup] Could not offer the crash report: {ex}");
        }
    }
}
