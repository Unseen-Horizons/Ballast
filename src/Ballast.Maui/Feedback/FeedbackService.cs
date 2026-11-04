// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Feedback;

namespace Ballast.Maui.Feedback;

/// <summary>
/// The device's side of feedback: the rider's words, with the build and the platform they came from, sent through the library's client.
/// "The map is empty" means something different on one build than on another, so the build is always sent.
/// </summary>
/// <remarks>
/// The client validates, trims and maps the answer. This class only adds what the device knows. The endpoint is anonymous, so a rider
/// who cannot get past sign-in can still report it.
/// </remarks>
public sealed class FeedbackService
{
    private readonly FeedbackClient _client;
    private readonly string _appVersion;
    private readonly string _platform;

    /// <summary>Creates the service.</summary>
    /// <param name="client">The library's feedback client.</param>
    /// <param name="appVersion">The app's version string, as the device reports it.</param>
    /// <param name="platform">The platform name, as the device reports it.</param>
    public FeedbackService(FeedbackClient client, string appVersion, string platform)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        _client = client;
        _appVersion = appVersion;
        _platform = platform;
    }

    /// <summary>Sends the feedback. An empty message is refused by the client and nothing is sent.</summary>
    /// <param name="message">What the rider wrote.</param>
    /// <param name="subject">An optional subject.</param>
    /// <param name="email">An optional address to answer on.</param>
    /// <param name="category">An optional category.</param>
    /// <param name="contextId">The id of the thing the feedback is about, when there is one.</param>
    /// <param name="contextName">The name of that thing, when there is one.</param>
    /// <param name="ct">Cancels the call.</param>
    public Task<FeedbackSendResult> SendAsync(
        string message,
        string? subject,
        string? email,
        string? category = null,
        int? contextId = null,
        string? contextName = null,
        CancellationToken ct = default) =>
        _client.SendAsync(
            new FeedbackRequest
            {
                Message = message,
                Subject = subject,
                Email = email,
                Category = category,
                ContextId = contextId,
                ContextName = contextName,
                AppVersion = _appVersion,
                Platform = _platform,
            },
            ct);
}
