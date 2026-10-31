// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Ballast.Errors;

/// <summary>
/// A deliberate refusal, such as a missing item, an invalid request or a refused credential. It carries the stable
/// <see cref="Code"/> that a client translates, the HTTP <see cref="StatusCode"/> it means, and its message. An API turns
/// it into an answer with <see cref="Problem.From(AppException, string?, string?)"/>.
/// </summary>
[SuppressMessage("Design", "CA1032", Justification = "Every error needs a code and a status, so the standard constructors that take neither are not offered.")]
public class AppException : Exception
{
    private static readonly IReadOnlyDictionary<string, object?> _noExtensions = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    private static readonly HashSet<string> _reservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "title", "status", "detail", "instance", "code", "traceId",
    };

    /// <summary>
    /// Creates a refusal.
    /// </summary>
    /// <param name="code">The stable code. Required, and a blank code is refused.</param>
    /// <param name="message">The message, which becomes the problem title. Required, and a blank message is refused.</param>
    /// <param name="status">The HTTP status, from 400 to 599. Required, with no default, so each throw says what it means.</param>
    /// <param name="detail">The developer sentence about this occurrence. When null, the problem detail is the message. A blank detail is refused.</param>
    /// <param name="extensions">
    /// Extra members for the answer. A name that is a problem member (<c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c>,
    /// <c>instance</c>, <c>code</c> or <c>traceId</c>, in any letter case) is refused. The dictionary is copied.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The code, the message or the detail is blank, or an extension name is blank or is a problem member name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is not from 400 to 599.</exception>
    public AppException(string code, string message, int status, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null)
        : base(RequireMessage(message))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfLessThan(status, 400);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599);
        if (detail is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        }

        Code = code;
        StatusCode = status;
        Detail = detail;
        Extensions = CopyExtensions(extensions);
    }

    /// <summary>The stable error code. A client translates this, never the message.</summary>
    public string Code { get; }

    /// <summary>The HTTP status this refusal means, from 400 to 599.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// The developer sentence about this occurrence, or null. When it is null, <see cref="Problem.From(AppException, string?, string?)"/>
    /// uses the message as the detail.
    /// </summary>
    public string? Detail { get; }

    /// <summary>Extra members for the answer. Never null: it is empty when none were given.</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; }

    /// <summary>A refusal with status 400, for a request that is malformed or invalid.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message, which becomes the problem title.</param>
    /// <param name="detail">The developer sentence, or null to use the message.</param>
    /// <param name="extensions">Extra members for the answer, or null for none.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentException">See the constructor.</exception>
    public static AppException BadRequest(string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, message, 400, detail, extensions);

    /// <summary>A refusal with status 403, for a caller who is known but may not do this.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message, which becomes the problem title.</param>
    /// <param name="detail">The developer sentence, or null to use the message.</param>
    /// <param name="extensions">Extra members for the answer, or null for none.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentException">See the constructor.</exception>
    public static AppException Forbidden(string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, message, 403, detail, extensions);

    /// <summary>A refusal with status 404, for an item that does not exist.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message, which becomes the problem title.</param>
    /// <param name="detail">The developer sentence, or null to use the message.</param>
    /// <param name="extensions">Extra members for the answer, or null for none.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentException">See the constructor.</exception>
    public static AppException NotFound(string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, message, 404, detail, extensions);

    /// <summary>A refusal with status 409, for a request that conflicts with the current state of the item.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message, which becomes the problem title.</param>
    /// <param name="detail">The developer sentence, or null to use the message.</param>
    /// <param name="extensions">Extra members for the answer, or null for none.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentException">See the constructor.</exception>
    public static AppException Conflict(string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, message, 409, detail, extensions);

    /// <summary>A refusal with status 422, for a request that is well formed but whose content cannot be processed.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message, which becomes the problem title.</param>
    /// <param name="detail">The developer sentence, or null to use the message.</param>
    /// <param name="extensions">Extra members for the answer, or null for none.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentException">See the constructor.</exception>
    public static AppException Unprocessable(string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, message, 422, detail, extensions);

    /// <summary>
    /// A refusal with status 502, for a failed call to another service. Use <see cref="UpstreamException"/> when the
    /// call's host, status and address should be kept.
    /// </summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message, which becomes the problem title.</param>
    /// <param name="detail">The developer sentence, or null to use the message.</param>
    /// <param name="extensions">Extra members for the answer, or null for none.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentException">See the constructor.</exception>
    public static AppException Upstream(string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, message, 502, detail, extensions);

    private static string RequireMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return message;
    }

    private static IReadOnlyDictionary<string, object?> CopyExtensions(IReadOnlyDictionary<string, object?>? extensions)
    {
        if (extensions is null)
        {
            return _noExtensions;
        }

        var copy = new Dictionary<string, object?>(extensions.Count, StringComparer.Ordinal);
        foreach (var (name, value) in extensions)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An extension name must not be blank.", nameof(extensions));
            }

            if (_reservedNames.Contains(name))
            {
                throw new ArgumentException($"'{name}' is a problem member name and cannot be used as an extension.", nameof(extensions));
            }

            copy[name] = value;
        }

        return new ReadOnlyDictionary<string, object?>(copy);
    }
}
