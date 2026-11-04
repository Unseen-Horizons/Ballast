// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ballast.AspNetCore;

/// <summary>
/// One rule that turns an exception into a problem answer. The rules are checked in the order they were added, and the first
/// whose <see cref="Matches"/> returns true answers the request. An <see cref="Ballast.Errors.AppException"/> is answered from
/// its own fields before any rule is checked, and an exception that no rule matches is answered with the internal error.
/// </summary>
public sealed class ExceptionMapping
{
    /// <summary>
    /// Creates a rule.
    /// </summary>
    /// <param name="matches">
    /// Returns true for an exception this rule answers. It receives the exception the handler caught, not its inner exceptions,
    /// so a rule that must find a failure deep in the chain walks <see cref="Exception.InnerException"/> itself.
    /// </param>
    /// <param name="status">The HTTP status the answer carries, from 400 to 599.</param>
    /// <param name="code">The stable code the answer carries. Required, and a blank code is refused.</param>
    /// <exception cref="ArgumentNullException"><paramref name="matches"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is not from 400 to 599.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null, empty or only white space.</exception>
    public ExceptionMapping(Func<Exception, bool> matches, int status, string code)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentOutOfRangeException.ThrowIfLessThan(status, 400, nameof(status));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599, nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(code, nameof(code));

        Matches = matches;
        Status = status;
        Code = code;
    }

    /// <summary>Returns true for an exception this rule answers.</summary>
    public Func<Exception, bool> Matches { get; }

    /// <summary>The HTTP status the answer carries, from 400 to 599.</summary>
    public int Status { get; }

    /// <summary>The stable code the answer carries.</summary>
    public string Code { get; }

    /// <summary>
    /// The title of the answer, or null to use <see cref="TitleFactory"/> or else <see cref="IProblemTitles"/>. Must not be blank
    /// when it is set. Ignored when <see cref="TitleFactory"/> is set.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Computes the title of the answer from the request and the exception. When set, it takes precedence over <see cref="Title"/>.
    /// Use it for a title that is localised per request.
    /// </summary>
    public Func<HttpContext, Exception, string>? TitleFactory { get; init; }

    /// <summary>
    /// Computes the detail of the answer from the request and the exception, or returns null for no detail. The detail is written
    /// to the client, so it must not carry the exception's own text unless that text is safe to show.
    /// </summary>
    public Func<HttpContext, Exception, string?>? DetailFactory { get; init; }

    /// <summary>
    /// The number of seconds to write in the <c>Retry-After</c> header, or null for no header. When set, it must be at least 1.
    /// </summary>
    public int? RetryAfterSeconds { get; init; }

    /// <summary>
    /// The level the failure is logged at, or null for the default: <see cref="LogLevel.Information"/> below status 500 and
    /// <see cref="LogLevel.Error"/> from 500. The exception and its stack trace are logged only at <see cref="LogLevel.Error"/>
    /// and above, so a condition logged at a lower level is logged with its type and message only.
    /// </summary>
    public LogLevel? LogLevel { get; init; }
}
