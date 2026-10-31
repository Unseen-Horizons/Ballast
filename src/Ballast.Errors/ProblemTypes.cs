// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Errors;

/// <summary>
/// The problem type address of each HTTP status. A status that RFC 9110 defines has its own section of that document, and
/// 429 has its section of RFC 6585. Every other status, 418 included, has <c>about:blank</c>, the RFC 9457 default for a
/// problem with no type of its own.
/// </summary>
public static class ProblemTypes
{
    /// <summary>
    /// The problem type address for an HTTP status. It never throws and never returns null.
    /// </summary>
    /// <param name="status">The HTTP status.</param>
    /// <returns>
    /// The address of the status's section in RFC 9110, or the section of RFC 6585 for 429, or <c>about:blank</c> for any
    /// other status.
    /// </returns>
    public static string For(int status) => status switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        401 => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        402 => "https://tools.ietf.org/html/rfc9110#section-15.5.3",
        403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        405 => "https://tools.ietf.org/html/rfc9110#section-15.5.6",
        406 => "https://tools.ietf.org/html/rfc9110#section-15.5.7",
        407 => "https://tools.ietf.org/html/rfc9110#section-15.5.8",
        408 => "https://tools.ietf.org/html/rfc9110#section-15.5.9",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        410 => "https://tools.ietf.org/html/rfc9110#section-15.5.11",
        411 => "https://tools.ietf.org/html/rfc9110#section-15.5.12",
        412 => "https://tools.ietf.org/html/rfc9110#section-15.5.13",
        413 => "https://tools.ietf.org/html/rfc9110#section-15.5.14",
        414 => "https://tools.ietf.org/html/rfc9110#section-15.5.15",
        415 => "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        416 => "https://tools.ietf.org/html/rfc9110#section-15.5.17",
        417 => "https://tools.ietf.org/html/rfc9110#section-15.5.18",
        421 => "https://tools.ietf.org/html/rfc9110#section-15.5.20",
        422 => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        426 => "https://tools.ietf.org/html/rfc9110#section-15.5.22",
        429 => "https://tools.ietf.org/html/rfc6585#section-4",
        500 => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        501 => "https://tools.ietf.org/html/rfc9110#section-15.6.2",
        502 => "https://tools.ietf.org/html/rfc9110#section-15.6.3",
        503 => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        504 => "https://tools.ietf.org/html/rfc9110#section-15.6.5",
        505 => "https://tools.ietf.org/html/rfc9110#section-15.6.6",
        _ => "about:blank",
    };
}
