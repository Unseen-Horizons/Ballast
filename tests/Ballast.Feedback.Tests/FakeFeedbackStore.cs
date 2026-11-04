// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Paging;

namespace Ballast.Feedback.Tests;

/// <summary>
/// An in-memory <see cref="IFeedbackStore"/> that behaves as the rules expect a store to: ids count up from 1, a list is newest first, a
/// filter matches the same rows the app's store would, and the page is built with the request's own page.
/// </summary>
public sealed class FakeFeedbackStore : IFeedbackStore
{
    private readonly List<FeedbackResponse> _rows = [];

    /// <summary>When set, every save throws it, so a test can prove that a failed save is not reported as a success.</summary>
    public Exception? FailSaves { get; set; }

    /// <summary>The number of rows saved.</summary>
    public int Count => _rows.Count;

    /// <summary>The saved rows, in the order they were saved.</summary>
    public IReadOnlyList<FeedbackResponse> Rows => _rows;

    /// <inheritdoc />
    public Task<int> AddAsync(NewFeedback feedback, CancellationToken ct = default)
    {
        if (FailSaves is not null)
        {
            throw FailSaves;
        }

        var row = new FeedbackResponse
        {
            Id = _rows.Count + 1,
            Message = feedback.Message,
            Subject = feedback.Subject,
            Email = feedback.Email,
            Category = feedback.Category,
            AppVersion = feedback.AppVersion,
            Platform = feedback.Platform,
            ContextId = feedback.ContextId,
            ContextName = feedback.ContextName,
            ReceivedAt = feedback.ReceivedAtUtc,
            Status = feedback.Status.ToString(),
        };
        _rows.Add(row);
        return Task.FromResult(row.Id);
    }

    /// <inheritdoc />
    public Task<PagedResult<FeedbackResponse>> ListAsync(FeedbackListQuery query, CancellationToken ct = default)
    {
        var matching = _rows
            .Where(r => query.Category is null || r.Category == query.Category)
            .Where(r => query.Status is null || r.Status == query.Status.Value.ToString())
            .OrderByDescending(r => r.Id)
            .ToList();

        var page = matching.Skip(query.Page.Skip).Take(query.Page.PerPage).ToList();
        return Task.FromResult(new PagedResult<FeedbackResponse>(page, matching.Count, query.Page));
    }

    /// <inheritdoc />
    public Task<bool> SetStatusAsync(int id, FeedbackStatus status, CancellationToken ct = default)
    {
        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            return Task.FromResult(false);
        }

        row.Status = status.ToString();
        return Task.FromResult(true);
    }
}
