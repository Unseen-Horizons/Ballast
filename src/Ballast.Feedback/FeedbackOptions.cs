// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Feedback;

/// <summary>
/// The settings of the feedback library. The defaults accept any category, as the apps' endpoint does today, and name the route
/// the client posts to. Set only what differs.
/// </summary>
public sealed class FeedbackOptions
{
    /// <summary>
    /// The path the client posts to, relative to its base address. Default <c>feedback</c>.
    /// </summary>
    public string Route { get; init; } = "feedback";

    /// <summary>
    /// The categories a report may carry, matched exactly. Empty (the default) accepts any category up to its cap. When it lists
    /// names, a report with another category is refused with <see cref="UnknownCategoryCode"/>. A report with no category is always
    /// accepted: its absence is not a category.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// The code of the 400 answer for a report that breaks a rule: a blank message, a value over its cap, or a reply address that is
    /// not an address. Default <c>FEEDBACK_INVALID</c>.
    /// </summary>
    public string InvalidCode { get; init; } = "FEEDBACK_INVALID";

    /// <summary>
    /// The code of the 400 answer for a category that is not in <see cref="Categories"/>. Default <c>FEEDBACK_CATEGORY_UNKNOWN</c>.
    /// </summary>
    public string UnknownCategoryCode { get; init; } = "FEEDBACK_CATEGORY_UNKNOWN";

    /// <summary>
    /// Checks the settings. A blank route or code, or a blank category in the list, is refused.
    /// </summary>
    /// <exception cref="ArgumentException">A setting is blank. The message names it.</exception>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Route);
        ArgumentException.ThrowIfNullOrWhiteSpace(InvalidCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(UnknownCategoryCode);
        ArgumentNullException.ThrowIfNull(Categories);
        foreach (var category in Categories)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
        }
    }
}
