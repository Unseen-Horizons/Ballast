// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;

namespace Ballast.Auth;

/// <summary>
/// The text of an Identity refusal, for the message of the error it becomes. Identity can refuse without naming a reason, and an
/// error's message must never be blank, so an empty join reads as <see cref="Refused"/>.
/// </summary>
public static class IdentityErrorText
{
    /// <summary>The message when Identity gave no reason.</summary>
    public const string Refused = "The request was refused.";

    /// <summary>The descriptions of the errors, joined with ", ", or <see cref="Refused"/> when there are none.</summary>
    /// <param name="errors">The errors Identity returned.</param>
    /// <returns>A message that is never blank.</returns>
    public static string Of(IEnumerable<IdentityError> errors) => Of(errors.Select(e => e.Description));

    /// <summary>The texts joined with ", ", or <see cref="Refused"/> when the join is blank.</summary>
    /// <param name="texts">The reasons, already text.</param>
    /// <returns>A message that is never blank.</returns>
    public static string Of(IEnumerable<string?> texts)
    {
        var text = string.Join(", ", texts);
        return string.IsNullOrWhiteSpace(text) ? Refused : text;
    }
}
