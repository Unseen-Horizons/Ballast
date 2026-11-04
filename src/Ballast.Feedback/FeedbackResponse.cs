// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Ballast.Feedback;

/// <summary>
/// A persisted feedback row, as the admin list returns it. Its JSON names are <c>id</c>, <c>message</c>, <c>subject</c>, <c>email</c>,
/// <c>category</c>, <c>appVersion</c>, <c>platform</c>, <c>stationId</c>, <c>stationName</c>, <c>receivedAt</c> and <c>status</c>.
/// </summary>
public class FeedbackResponse
{
    /// <summary>The row's id.</summary>
    public int Id { get; set; }

    /// <summary>The message, trimmed.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The subject, or null.</summary>
    public string? Subject { get; set; }

    /// <summary>The reply address, or null.</summary>
    public string? Email { get; set; }

    /// <summary>The category, or null.</summary>
    public string? Category { get; set; }

    /// <summary>The app version the app reported, or null.</summary>
    public string? AppVersion { get; set; }

    /// <summary>The platform the app reported, or null.</summary>
    public string? Platform { get; set; }

    /// <summary>The id of the item the report was filed from, or null. Its JSON name is <c>stationId</c>.</summary>
    [JsonPropertyName("stationId")]
    public int? ContextId { get; set; }

    /// <summary>The name of that item, or null. Its JSON name is <c>stationName</c>.</summary>
    [JsonPropertyName("stationName")]
    public string? ContextName { get; set; }

    /// <summary>When the message was received, in UTC.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>The status's name: <c>New</c>, <c>InReview</c>, <c>Handled</c> or <c>Dismissed</c>.</summary>
    public string Status { get; set; } = string.Empty;
}
