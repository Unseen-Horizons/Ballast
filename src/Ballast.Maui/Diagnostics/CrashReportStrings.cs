// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Diagnostics;

/// <summary>
/// The words the crash offer shows. The English values are the defaults; the app passes its own localised ones, which is why the name of the app
/// appears in none of them.
/// </summary>
public sealed record CrashReportStrings
{
    /// <summary>The question. The English default: "The app closed unexpectedly last time. Send a crash report?"</summary>
    public string Question { get; init; } = "The app closed unexpectedly last time. Send a crash report?";

    /// <summary>The button that sends the report.</summary>
    public string Send { get; init; } = "Send";

    /// <summary>The button that shows the exact text first.</summary>
    public string SeeWhatsSent { get; init; } = "See what's sent";

    /// <summary>The button that does not send the report.</summary>
    public string DontSend { get; init; } = "Don't send";

    /// <summary>The title of the dialog that shows the exact text.</summary>
    public string WhatsSentTitle { get; init; } = "What is sent";

    /// <summary>The title of the thank-you dialog.</summary>
    public string SentTitle { get; init; } = "Thank you";

    /// <summary>The body of the thank-you dialog.</summary>
    public string SentBody { get; init; } = "The crash report was sent.";

    /// <summary>The title of the failure dialog.</summary>
    public string ErrorTitle { get; init; } = "Error";

    /// <summary>The text of the failure dialog: the send failed, and the report is kept.</summary>
    public string SendFailed { get; init; } = "The crash report could not be sent. It is kept and will be offered again.";

    /// <summary>The plain confirm button of the two plain messages.</summary>
    public string Ok { get; init; } = "OK";
}
