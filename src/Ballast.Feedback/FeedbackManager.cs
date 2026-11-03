// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel.DataAnnotations;

using Ballast.Errors;
using Ballast.Paging;

using Microsoft.Extensions.Logging;

namespace Ballast.Feedback;

/// <summary>
/// Takes feedback in, checks it against the caps and the category list, and reads it back for triage. The rules live here; the
/// storage is the app's <see cref="IFeedbackStore"/>. The endpoints that call it stay in the app, because they are the app's routes
/// and its authorization.
/// </summary>
public sealed class FeedbackManager
{
    private static readonly EmailAddressAttribute _emailRule = new();

    private readonly IFeedbackStore _store;
    private readonly ILogger<FeedbackManager> _logger;
    private readonly FeedbackOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the manager over a store.</summary>
    /// <param name="store">Where the messages are kept.</param>
    /// <param name="logger">Where a stored message is logged.</param>
    /// <param name="options">The settings, or null for the defaults.</param>
    /// <param name="timeProvider">The clock that stamps a message's receipt, or null for the system clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="logger"/> is null.</exception>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is blank.</exception>
    public FeedbackManager(IFeedbackStore store, ILogger<FeedbackManager> logger, FeedbackOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options ?? new FeedbackOptions();
        _options.Validate();
        _store = store;
        _logger = logger;
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Checks a message, trims its text and saves it as <see cref="FeedbackStatus.New"/>. Text that is blank after trimming is stored as
    /// null, except the message, which is required.
    /// </summary>
    /// <param name="request">The message.</param>
    /// <param name="ct">Cancels the save.</param>
    /// <returns>The saved message, with its id and its receipt time in UTC.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="AppException">
    /// 400 with the invalid code when the message is blank, a value is over its cap, or the reply address is not an address. 400 with the
    /// unknown-category code when the category is not in the configured list. The message is not saved.
    /// </exception>
    public async Task<FeedbackResponse> SubmitAsync(FeedbackRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Check(request);

        var entry = new NewFeedback(
            Message: request.Message.Trim(),
            Subject: Trimmed(request.Subject),
            Email: Trimmed(request.Email),
            Category: Trimmed(request.Category),
            AppVersion: request.AppVersion,
            Platform: request.Platform,
            ContextId: request.ContextId,
            ContextName: Trimmed(request.ContextName),
            ReceivedAtUtc: _clock.GetUtcNow().UtcDateTime,
            Status: FeedbackStatus.New);

        var id = await _store.AddAsync(entry, ct);
        _logger.LogInformation("Stored feedback {Id} (category {Category})", id, entry.Category ?? "-");

        return new FeedbackResponse
        {
            Id = id,
            Message = entry.Message,
            Subject = entry.Subject,
            Email = entry.Email,
            Category = entry.Category,
            AppVersion = entry.AppVersion,
            Platform = entry.Platform,
            ContextId = entry.ContextId,
            ContextName = entry.ContextName,
            ReceivedAt = entry.ReceivedAtUtc,
            Status = entry.Status.ToString(),
        };
    }

    /// <summary>
    /// Returns one page of messages, newest first, filtered by category and status. A blank category is no filter.
    /// </summary>
    /// <param name="page">The 1-based page number. Must be 1 or more.</param>
    /// <param name="perPage">The number of rows on a page. Must be 1 or more. The upper limit is the caller's to check.</param>
    /// <param name="category">Only messages in this category, or null or blank for every category.</param>
    /// <param name="status">Only messages with this status, or null for every status.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The page, with the total number of messages the filters match.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> or <paramref name="perPage"/> is below 1.</exception>
    public Task<PagedResult<FeedbackResponse>> ListAsync(int page, int perPage, string? category, FeedbackStatus? status, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);

        var query = new FeedbackListQuery(
            new PageRequest { Page = page, PerPage = perPage },
            string.IsNullOrWhiteSpace(category) ? null : category,
            status);
        return _store.ListAsync(query, ct);
    }

    /// <summary>
    /// Sets a message's status. The store clears any review mark the app keeps on the row.
    /// </summary>
    /// <param name="id">The message's id.</param>
    /// <param name="status">The new status.</param>
    /// <param name="ct">Cancels the change.</param>
    /// <returns>True when the message exists and was changed; false when there is no message with that id.</returns>
    /// <exception cref="AppException">400 with the invalid code when <paramref name="status"/> is not one of the defined statuses.</exception>
    public Task<bool> SetStatusAsync(int id, FeedbackStatus status, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status))
        {
            throw AppException.BadRequest(_options.InvalidCode, "Unknown feedback status.", $"status {(int)status} is not a feedback status.");
        }

        return _store.SetStatusAsync(id, status, ct);
    }

    private void Check(FeedbackRequest request)
    {
        var problem = FirstProblem(request);
        if (problem is not null)
        {
            throw AppException.BadRequest(_options.InvalidCode, "Feedback is not valid.", problem);
        }

        if (_options.Categories.Count > 0
            && !string.IsNullOrWhiteSpace(request.Category)
            && !_options.Categories.Contains(request.Category.Trim()))
        {
            throw AppException.BadRequest(
                _options.UnknownCategoryCode,
                "Unknown feedback category.",
                $"category must be one of: {string.Join(", ", _options.Categories)}.");
        }
    }

    /// <summary>
    /// The first rule the request breaks, as a sentence that names the field, or null when none is broken. The caps are measured on the
    /// text as it was sent, before any trimming, which is how a model binder measures them.
    /// </summary>
    private static string? FirstProblem(FeedbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return "message is required.";
        }

        return TooLong(request.Message, FeedbackLimits.MessageMaxLength, "message")
            ?? TooLong(request.Subject, FeedbackLimits.SubjectMaxLength, "subject")
            ?? TooLong(request.Email, FeedbackLimits.EmailMaxLength, "email")
            ?? (_emailRule.IsValid(request.Email) ? null : "email must be an e-mail address.")
            ?? TooLong(request.AppVersion, FeedbackLimits.AppVersionMaxLength, "appVersion")
            ?? TooLong(request.Platform, FeedbackLimits.PlatformMaxLength, "platform")
            ?? TooLong(request.Category, FeedbackLimits.CategoryMaxLength, "category")
            ?? TooLong(request.ContextName, FeedbackLimits.ContextNameMaxLength, "contextName");
    }

    private static string? TooLong(string? value, int max, string name) =>
        value is not null && value.Length > max ? $"{name} must be {max} characters or fewer." : null;

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
