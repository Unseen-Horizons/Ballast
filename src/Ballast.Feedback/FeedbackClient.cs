// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Json;

using Ballast.Errors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.Feedback;

/// <summary>
/// Sends a user's message to the app's API, which keeps it for triage. The destination is the server's, so no address ships on a device.
/// <para>
/// A failure is reported as an outcome, never as a success: the caller shows the outcome's words and keeps the text, so a message is
/// not lost to a dropped connection. The endpoint is anonymous, so a guest who cannot get past sign-in can still report it.
/// </para>
/// </summary>
public sealed class FeedbackClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FeedbackClient> _logger;
    private readonly string _route;

    /// <summary>Creates the client over an <see cref="HttpClient"/> whose base address is the API's.</summary>
    /// <param name="http">The client to post with. Its base address is the API's root.</param>
    /// <param name="logger">Where a failed send is logged, or null for none.</param>
    /// <param name="options">The settings. Only <see cref="FeedbackOptions.Route"/> is read here. Null uses the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="http"/> is null.</exception>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is blank.</exception>
    public FeedbackClient(HttpClient http, ILogger<FeedbackClient>? logger = null, FeedbackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        var resolved = options ?? new FeedbackOptions();
        resolved.Validate();
        _http = http;
        _logger = logger ?? NullLogger<FeedbackClient>.Instance;
        _route = resolved.Route;
    }

    /// <summary>
    /// Posts the message. The text is trimmed, and text that is blank after trimming is sent as absent, as the server expects. A blank
    /// message is not posted at all.
    /// </summary>
    /// <param name="request">The message. Its app version and platform are the app's to fill in.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>How the send ended. See <see cref="FeedbackSendResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async Task<FeedbackSendResult> SendAsync(FeedbackRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return new FeedbackSendResult(FeedbackSendOutcome.Empty, FeedbackCodes.Empty);
        }

        var outgoing = new FeedbackRequest
        {
            Message = request.Message.Trim(),
            Subject = Trimmed(request.Subject),
            Email = Trimmed(request.Email),
            Category = Trimmed(request.Category),
            ContextId = request.ContextId,
            ContextName = Trimmed(request.ContextName),

            // Which build it came from. "The map is empty" means something different on one build than on another.
            AppVersion = request.AppVersion,
            Platform = request.Platform,
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(_route, outgoing, FeedbackJsonContext.Default.FeedbackRequest, ct);
            if (response.IsSuccessStatusCode)
            {
                return new FeedbackSendResult(FeedbackSendOutcome.Sent, string.Empty);
            }

            // 501 means the server has nowhere to send it: a deployment problem, not the user's.
            if (response.StatusCode == HttpStatusCode.NotImplemented)
            {
                var notConfigured = await ProblemReader.TryReadAsync(response, ct);
                _logger.LogWarning("Feedback answered 501: {Detail}", notConfigured?.Detail ?? notConfigured?.Title ?? "not configured");
                return Failure(FeedbackSendOutcome.NotConfigured, FeedbackCodes.NotConfigured, notConfigured);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var notFound = await ProblemReader.TryReadAsync(response, ct);
                _logger.LogWarning("Feedback answered 404: {Detail}", notFound?.Detail ?? notFound?.Title ?? "not found");
                return Failure(FeedbackSendOutcome.NotAvailable, FeedbackCodes.NotAvailable, notFound);
            }

            var problem = await ProblemReader.TryReadAsync(response, ct);
            if (problem is not null)
            {
                _logger.LogWarning(
                    "Feedback answered {Status}: {Detail} (code {Code}, trace {TraceId})",
                    (int)response.StatusCode,
                    problem.Detail ?? problem.Title,
                    problem.Code,
                    problem.TraceId);
            }
            else
            {
                _logger.LogWarning("Feedback answered {Status} with no problem details.", (int)response.StatusCode);
            }

            return Failure(FeedbackSendOutcome.Rejected, FeedbackCodes.Rejected, problem);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Feedback could not reach the server.");
            return new FeedbackSendResult(FeedbackSendOutcome.Unreachable, FeedbackCodes.Unreachable);
        }
    }

    private static FeedbackSendResult Failure(FeedbackSendOutcome outcome, string fallbackCode, ProblemInfo? problem) =>
        new(outcome, problem?.Code ?? fallbackCode, problem?.Title, problem?.Detail, problem?.TraceId);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
