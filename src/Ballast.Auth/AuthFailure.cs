// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

namespace Ballast.Auth;

/// <summary>
/// One refusal the library can give: the stable code a client translates, the HTTP status it means, and the message. The code is
/// a public contract, so an app keeps the codes it already shipped and changes only the text or the status when it must.
/// </summary>
/// <param name="Code">The stable error code.</param>
/// <param name="Status">The HTTP status, from 400 to 599.</param>
/// <param name="Message">The message, which becomes the problem title. Never blank.</param>
public sealed record AuthFailure(string Code, int Status, string Message)
{
    /// <summary>Makes the refusal as an <see cref="AppException"/>, which the app's error handler turns into a problem answer.</summary>
    /// <param name="detail">A developer sentence about this occurrence, or null to use the message.</param>
    /// <returns>The exception to throw.</returns>
    /// <exception cref="ArgumentException">The code, the message or the detail is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The status is not from 400 to 599.</exception>
    public AppException ToException(string? detail = null) => new(Code, Message, Status, detail);
}
