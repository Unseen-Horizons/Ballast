// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Diagnostics;
using Ballast.Maui.Dialogs;

namespace Ballast.Maui.Adapters;

/// <summary>
/// The crash offer's screens, as MAUI pages: an action sheet with Send, See what's sent and Don't send, a dialog that shows the exact text, and
/// two plain messages. A close that is not one of the buttons (back button, a tap outside) answers <see cref="CrashPromptAnswer.Dismissed"/>, which
/// keeps the report.
/// </summary>
public sealed class MauiCrashReportUi : ICrashReportUi
{
    private readonly CrashReportStrings _strings;

    /// <summary>Creates the screens with the app's words.</summary>
    /// <param name="strings">The words the screens show.</param>
    public MauiCrashReportUi(CrashReportStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        _strings = strings;
    }

    /// <inheritdoc />
    public async Task<CrashPromptAnswer> AskAsync()
    {
        var picked = await OnTopPageAsync(page => page.DisplayActionSheetAsync(_strings.Question, _strings.DontSend, null, _strings.Send, _strings.SeeWhatsSent));

        if (picked == _strings.Send)
            return CrashPromptAnswer.Send;
        if (picked == _strings.SeeWhatsSent)
            return CrashPromptAnswer.SeeFirst;
        if (picked == _strings.DontSend)
            return CrashPromptAnswer.DontSend;
        return CrashPromptAnswer.Dismissed;
    }

    /// <inheritdoc />
    public async Task<CrashPromptAnswer> ShowTextAsync(string reportText)
    {
        // Accept is Send, cancel is Don't send; a dialog closed another way is not an answer.
        var wantsToSend = await OnTopPageAsync(page => page.DisplayAlertAsync(_strings.WhatsSentTitle, reportText, _strings.Send, _strings.DontSend));
        return wantsToSend ? CrashPromptAnswer.Send : CrashPromptAnswer.DontSend;
    }

    /// <inheritdoc />
    public Task ShowSentAsync() =>
        OnTopPageAsync(page => page.DisplayAlertAsync(_strings.SentTitle, _strings.SentBody, _strings.Ok));

    /// <inheritdoc />
    public Task ShowSendFailedAsync() =>
        OnTopPageAsync(page => page.DisplayAlertAsync(_strings.ErrorTitle, _strings.SendFailed, _strings.Ok));

    /// <summary>Runs <paramref name="show"/> on the main thread against the page the person is looking at (a modal over the shell wins).</summary>
    private static Task<T> OnTopPageAsync<T>(Func<Microsoft.Maui.Controls.Page, Task<T>> show) =>
        Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() => show(TopPage()));

    private static Task OnTopPageAsync(Func<Microsoft.Maui.Controls.Page, Task> show) =>
        Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() => show(TopPage()));

    private static Microsoft.Maui.Controls.Page TopPage()
    {
        var windows = Microsoft.Maui.Controls.Application.Current?.Windows;
        var window = windows is { Count: > 0 }
            ? windows[0]
            : throw new InvalidOperationException("There is no window to show the crash report question in.");
        var page = window.Page
            ?? throw new InvalidOperationException("The window has no page to show the crash report question over.");
        var modals = page.Navigation.ModalStack;
        return modals.Count > 0 ? modals[^1] : page;
    }
}

/// <summary>The settled-screen check for a MAUI shell: the shell's current page is loaded and no modal sits over it.</summary>
public static class MauiSettledScreen
{
    /// <summary>A settled-screen wait that asks the shell, on the UI thread.</summary>
    /// <returns>The wait, with the default number of attempts and pause.</returns>
    public static SettledScreen Create() =>
        new(() => Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() =>
            Microsoft.Maui.Controls.Shell.Current is { CurrentPage: { IsLoaded: true } } shell && shell.Navigation.ModalStack.Count == 0));
}
