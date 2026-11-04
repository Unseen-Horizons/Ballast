// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AspNetCore;

/// <summary>
/// Gives the title of a problem answer for its status and code. An application that localises its answers registers its own
/// implementation before or after <see cref="ProblemServiceExtensions.AddProblemResponses"/>; the default is
/// <see cref="EnglishProblemTitles"/>. The title is used for the problems the library makes: a validation failure, the unknown
/// route, the rate-limit rejection, the internal error, and a mapped exception without its own title. An
/// <see cref="Ballast.Errors.AppException"/> keeps its own message as its title.
/// </summary>
public interface IProblemTitles
{
    /// <summary>The title for a problem with this status and code.</summary>
    /// <param name="status">The HTTP status of the answer.</param>
    /// <param name="code">The stable code of the answer.</param>
    /// <returns>A title. It is never null or blank.</returns>
    string Title(int status, string code);
}
