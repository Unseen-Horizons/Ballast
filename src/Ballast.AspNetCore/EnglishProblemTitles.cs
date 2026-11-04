// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

using Microsoft.AspNetCore.WebUtilities;

namespace Ballast.AspNetCore;

/// <summary>
/// The default <see cref="IProblemTitles"/>: the English reason phrase of the status, such as "Not Found" for 404. A validation
/// failure has its own title, <c>One or more validation errors occurred.</c>, which is the title the framework has always given it.
/// </summary>
public sealed class EnglishProblemTitles : IProblemTitles
{
    /// <summary>The title of a validation failure.</summary>
    public const string ValidationTitle = "One or more validation errors occurred.";

    private const string FallbackTitle = "Error";

    /// <summary>
    /// Returns the English title for the status and code. It never throws and never returns null or blank: a status with no
    /// reason phrase is titled "Error".
    /// </summary>
    /// <param name="status">The HTTP status of the answer.</param>
    /// <param name="code">The stable code of the answer.</param>
    /// <returns>The title.</returns>
    public string Title(int status, string code)
    {
        if (string.Equals(code, ErrorCodes.ValidationFailed, StringComparison.Ordinal))
        {
            return ValidationTitle;
        }

        var phrase = ReasonPhrases.GetReasonPhrase(status);
        return string.IsNullOrWhiteSpace(phrase) ? FallbackTitle : phrase;
    }
}
