// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Feedback;

/// <summary>
/// A message as the manager hands it to the store, before the store has given it an id. The manager has already trimmed the text
/// and checked the caps; the store saves the values as they are.
/// </summary>
/// <param name="Message">The trimmed message.</param>
/// <param name="Subject">The trimmed subject, or null.</param>
/// <param name="Email">The trimmed reply address, or null.</param>
/// <param name="Category">The trimmed category, or null.</param>
/// <param name="AppVersion">The app version as the app reported it, or null.</param>
/// <param name="Platform">The platform as the app reported it, or null.</param>
/// <param name="ContextId">The id of the item the report was filed from, or null.</param>
/// <param name="ContextName">The trimmed name of that item, or null.</param>
/// <param name="ReceivedAtUtc">When the message was received, in UTC.</param>
/// <param name="Status">The status to save. The manager always passes <see cref="FeedbackStatus.New"/>.</param>
public sealed record NewFeedback(
    string Message,
    string? Subject,
    string? Email,
    string? Category,
    string? AppVersion,
    string? Platform,
    int? ContextId,
    string? ContextName,
    DateTime ReceivedAtUtc,
    FeedbackStatus Status);
