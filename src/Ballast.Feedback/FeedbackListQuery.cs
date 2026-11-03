// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Paging;

namespace Ballast.Feedback;

/// <summary>
/// One page of the feedback list, newest first, filtered by category and status when they are given.
/// </summary>
/// <param name="Page">The page to return: its 1-based number and its size. The store takes its rows from <see cref="PageRequest.Skip"/>.</param>
/// <param name="Category">Only messages in this category, or null for every category.</param>
/// <param name="Status">Only messages with this status, or null for every status.</param>
public sealed record FeedbackListQuery(PageRequest Page, string? Category, FeedbackStatus? Status);
