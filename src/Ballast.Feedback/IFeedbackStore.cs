// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Paging;

namespace Ballast.Feedback;

/// <summary>
/// Where feedback is kept. The app implements it over its own database (the library has no Entity Framework Core), and the
/// <see cref="FeedbackManager"/> holds the rules over it.
/// <para>
/// An implementation must not fail quietly: a save that did not happen throws, so the caller answers with an error, never with
/// a success that stored nothing. A status change also clears any review mark the app keeps on the row, because a new status replaces
/// the earlier review: the library keeps no such mark, so the store does it in <see cref="SetStatusAsync"/>.
/// </para>
/// </summary>
public interface IFeedbackStore
{
    /// <summary>Saves a new message.</summary>
    /// <param name="feedback">The message to save, already trimmed and checked.</param>
    /// <param name="ct">Cancels the save.</param>
    /// <returns>The id the store gave the row.</returns>
    Task<int> AddAsync(NewFeedback feedback, CancellationToken ct = default);

    /// <summary>
    /// Returns one page of messages, newest first. The store builds the page with <c>new PagedResult&lt;FeedbackResponse&gt;(rows, total, query.Page)</c>,
    /// where <c>rows</c> are the messages from <see cref="PageRequest.Skip"/> on, and <c>total</c> counts every message the filters match.
    /// </summary>
    /// <param name="query">The page and the filters.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<FeedbackResponse>> ListAsync(FeedbackListQuery query, CancellationToken ct = default);

    /// <summary>
    /// Sets a message's status. The store also clears any review mark the app keeps on the row, because a new status replaces the
    /// earlier review.
    /// </summary>
    /// <param name="id">The message's id.</param>
    /// <param name="status">The new status.</param>
    /// <param name="ct">Cancels the change.</param>
    /// <returns>True when the message exists and was changed; false when there is no message with that id.</returns>
    Task<bool> SetStatusAsync(int id, FeedbackStatus status, CancellationToken ct = default);
}
