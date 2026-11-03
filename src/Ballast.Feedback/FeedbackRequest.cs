// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ballast.Feedback;

/// <summary>
/// A message from a user, sent from the app's feedback screen. Its JSON names are <c>message</c>, <c>subject</c>, <c>email</c>,
/// <c>appVersion</c>, <c>platform</c>, <c>category</c>, <c>stationId</c> and <c>stationName</c>, and the apps send them, so they do not
/// change. The length caps are in <see cref="FeedbackLimits"/>. They are also validation attributes (<c>StringLength</c>, which is
/// trim-safe), so an ASP.NET Core app refuses an over-long request at model binding as before, and <see cref="FeedbackManager"/>
/// refuses a value over its cap for any other caller.
/// <para>
/// The app supplies the app version and the platform, so "the map is empty" can be read against the build it came from. They are
/// passed in rather than inferred, because the platform's HTTP client does not set a user agent that says anything useful.
/// </para>
/// </summary>
public class FeedbackRequest
{
    /// <summary>The message. Required: a blank message is refused.</summary>
    [Required]
    [StringLength(FeedbackLimits.MessageMaxLength)]
    public string Message { get; set; } = string.Empty;

    /// <summary>An optional subject.</summary>
    [StringLength(FeedbackLimits.SubjectMaxLength)]
    public string? Subject { get; set; }

    /// <summary>
    /// Where a reply would go. Optional on purpose: someone reporting a bug should not have to hand over an address to do it.
    /// </summary>
    [EmailAddress]
    [StringLength(FeedbackLimits.EmailMaxLength)]
    public string? Email { get; set; }

    /// <summary>The app's version, as the app reports it.</summary>
    [StringLength(FeedbackLimits.AppVersionMaxLength)]
    public string? AppVersion { get; set; }

    /// <summary>The platform, as the app reports it.</summary>
    [StringLength(FeedbackLimits.PlatformMaxLength)]
    public string? Platform { get; set; }

    /// <summary>The category. Any text up to the cap is accepted unless <see cref="FeedbackOptions.Categories"/> lists the allowed ones.</summary>
    [StringLength(FeedbackLimits.CategoryMaxLength)]
    public string? Category { get; set; }

    /// <summary>
    /// The id of the item the report was filed from, when the app attached one (for example a place it was showing). Attribution
    /// only: it is omitted when the user dismisses the attachment. Its JSON name is <c>stationId</c>.
    /// </summary>
    [JsonPropertyName("stationId")]
    public int? ContextId { get; set; }

    /// <summary>The name of that item, when there is one. Its JSON name is <c>stationName</c>.</summary>
    [JsonPropertyName("stationName")]
    [StringLength(FeedbackLimits.ContextNameMaxLength)]
    public string? ContextName { get; set; }
}
