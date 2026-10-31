// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;

namespace Ballast.Errors;

/// <summary>
/// The answer an API writes for a failed request, with the members RFC 9457 describes: type, title, status and detail,
/// plus the stable <see cref="Code"/>, the trace reference and any extension members. This is a plain record with no
/// framework dependency. Ballast.AspNetCore writes it into an HTTP response.
/// </summary>
/// <remarks>
/// This record is the shape, not the wire format. Serializing it directly with System.Text.Json puts
/// <see cref="Extensions"/> under an <c>extensions</c> member. The HTTP form, which Ballast.AspNetCore writes, puts each
/// extension at the top level, as ASP.NET's ProblemDetails does.
/// </remarks>
public sealed record Problem
{
    private static readonly IReadOnlyDictionary<string, object?> _noExtensions = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    /// <summary>The problem type address, from <see cref="ProblemTypes.For"/>.</summary>
    public required string Type { get; init; }

    /// <summary>The short title. For an <see cref="AppException"/>, this is its message.</summary>
    public required string Title { get; init; }

    /// <summary>The HTTP status.</summary>
    public required int Status { get; init; }

    /// <summary>The developer sentence about this occurrence, or null.</summary>
    public string? Detail { get; init; }

    /// <summary>The address of the request that failed, or null.</summary>
    public string? Instance { get; init; }

    /// <summary>The stable error code. A client translates this.</summary>
    public required string Code { get; init; }

    /// <summary>The trace reference that joins this answer to the server's log, or null.</summary>
    public string? TraceId { get; init; }

    /// <summary>Extra members for the answer. Never null: it is empty when there are none.</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } = _noExtensions;

    /// <summary>
    /// The answer for an <see cref="AppException"/>. The title is its message. The detail is its detail, or its message when
    /// it has none. The type comes from its status, and the code and extensions are its own. The exception's
    /// <see cref="UpstreamException"/> fields, when it is one, are not written.
    /// </summary>
    /// <param name="exception">The refusal.</param>
    /// <param name="traceId">The trace reference, or null when the caller has none.</param>
    /// <param name="instance">The address of the request, or null.</param>
    /// <returns>The problem.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static Problem From(AppException exception, string? traceId = null, string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new Problem
        {
            Type = ProblemTypes.For(exception.StatusCode),
            Title = exception.Message,
            Status = exception.StatusCode,
            Detail = exception.Detail ?? exception.Message,
            Instance = instance,
            Code = exception.Code,
            TraceId = traceId,
            Extensions = exception.Extensions,
        };
    }

    /// <summary>
    /// The answer for an unexpected failure: status 500, the title "Internal Server Error", the code
    /// <see cref="ErrorCodes.InternalError"/>, and a detail that holds only the trace reference. It takes no exception, so
    /// a database error number, a stack trace or any other text of the failure cannot reach the client.
    /// </summary>
    /// <param name="traceId">The trace reference that the server's log holds for the failure. Required, and a blank value is refused.</param>
    /// <param name="instance">The address of the request, or null.</param>
    /// <returns>The problem.</returns>
    /// <exception cref="ArgumentException"><paramref name="traceId"/> is null, empty or only white space.</exception>
    public static Problem InternalError(string traceId, string? instance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);
        return new Problem
        {
            Type = ProblemTypes.For(500),
            Title = "Internal Server Error",
            Status = 500,
            Detail = $"Reference {traceId}",
            Instance = instance,
            Code = ErrorCodes.InternalError,
            TraceId = traceId,
        };
    }
}
