// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Text.Json;

using Ballast.Errors;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.AspNetCore;

/// <summary>
/// Writes the problem answers of an API. Every answer is <c>application/problem+json</c>, carries the stable <c>code</c> and the
/// <c>traceId</c>, and has the trace reference in the <c>X-Trace-Id</c> header. The members are the ones the framework's problem
/// details have always written: <c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c> and <c>instance</c>, each left out when it is
/// null, then <c>code</c>, <c>traceId</c> and the extension members at the top level.
/// </summary>
public static class ProblemResponses
{
    /// <summary>The content type of every problem answer.</summary>
    public const string ContentType = "application/problem+json; charset=utf-8";

    /// <summary>The header that carries the trace reference of every response.</summary>
    public const string TraceIdHeader = "X-Trace-Id";

    private const int ValidationStatus = 400;

    private static readonly IProblemTitles _defaultTitles = new EnglishProblemTitles();

    /// <summary>
    /// The trace reference of a request: the current activity's id, or the request's trace identifier when there is no activity.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>The trace reference. It is never null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static string TraceIdOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Activity.Current?.Id ?? context.TraceIdentifier;
    }

    /// <summary>
    /// Writes a problem as the response: its status, the trace header and its JSON body. The problem's own trace reference is
    /// written in the header, or the request's when the problem has none.
    /// </summary>
    /// <param name="context">The request to answer.</param>
    /// <param name="problem">The problem to write.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when the body is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="problem"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The response has already started, so the status cannot be set.</exception>
    public static Task WriteAsync(HttpContext context, Problem problem, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problem);
        RequireNotStarted(context);

        var response = context.Response;
        response.StatusCode = problem.Status;
        response.Headers[TraceIdHeader] = problem.TraceId ?? TraceIdOf(context);
        return WriteBodyAsync(response, ToDetails(problem), ct);
    }

    /// <summary>
    /// Builds the answer for a validation failure, with the same members as the framework's answer for a rejected request body:
    /// the title from <see cref="IProblemTitles"/>, status 400, the <c>errors</c> member, then <c>traceId</c> and <c>code</c>
    /// (<see cref="Ballast.Errors.ErrorCodes.ValidationFailed"/>). It is for a validation the application runs itself. A
    /// rejected model is answered by the framework and needs no call.
    /// </summary>
    /// <param name="context">The request that failed validation.</param>
    /// <param name="errors">The messages, by field name. Each field needs at least one message.</param>
    /// <returns>The answer. It is not written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="errors"/> is null, or a message list is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="errors"/> has no field, a field name is blank, or a field has no message.
    /// </exception>
    public static ValidationProblemDetails CreateValidation(HttpContext context, IReadOnlyDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("A validation answer must name at least one field.", nameof(errors));
        }

        var fields = new Dictionary<string, string[]>(errors.Count, StringComparer.Ordinal);
        foreach (var (field, messages) in errors)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field, nameof(errors));
            ArgumentNullException.ThrowIfNull(messages, nameof(errors));
            if (messages.Length == 0)
            {
                throw new ArgumentException($"The field '{field}' has no message.", nameof(errors));
            }

            fields[field] = [.. messages];
        }

        var details = new ValidationProblemDetails(fields)
        {
            Type = ProblemTypes.For(ValidationStatus),
            Title = TitlesOf(context).Title(ValidationStatus, ErrorCodes.ValidationFailed),
            Status = ValidationStatus,
        };
        details.Extensions["traceId"] = TraceIdOf(context);
        details.Extensions["code"] = ErrorCodes.ValidationFailed;
        return details;
    }

    /// <summary>
    /// Builds a validation answer with <see cref="CreateValidation"/> and writes it as the response, with the 400 status and the
    /// trace header, exactly as <see cref="WriteAsync"/> writes a problem.
    /// </summary>
    /// <param name="context">The request that failed validation.</param>
    /// <param name="errors">The messages, by field name. Each field needs at least one message.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when the body is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="errors"/> is null, or a message list is null.</exception>
    /// <exception cref="ArgumentException">See <see cref="CreateValidation"/>.</exception>
    /// <exception cref="InvalidOperationException">The response has already started.</exception>
    public static Task WriteValidationAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors, CancellationToken ct = default)
    {
        var details = CreateValidation(context, errors);
        RequireNotStarted(context);

        context.Response.StatusCode = ValidationStatus;
        context.Response.Headers[TraceIdHeader] = TraceIdOf(context);
        return WriteBodyAsync(context.Response, details, ct);
    }

    /// <summary>
    /// The titles the request should use: the registered <see cref="IProblemTitles"/>, or the English titles when none is registered
    /// or the request has no services.
    /// </summary>
    internal static IProblemTitles TitlesOf(HttpContext context) =>
        context.RequestServices?.GetService<IProblemTitles>() ?? _defaultTitles;

    /// <summary>The framework's problem details for a problem: null members are left out by the serializer.</summary>
    internal static ProblemDetails ToDetails(Problem problem)
    {
        var details = new ProblemDetails
        {
            Type = problem.Type,
            Title = problem.Title,
            Status = problem.Status,
            Detail = problem.Detail,
            Instance = problem.Instance,
        };

        // The code comes before the trace reference, as it always has; the extensions follow, and a name that is already
        // written is not replaced.
        details.Extensions["code"] = problem.Code;
        if (problem.TraceId is not null)
        {
            details.Extensions["traceId"] = problem.TraceId;
        }

        foreach (var (name, value) in problem.Extensions)
        {
            if (!details.Extensions.ContainsKey(name))
            {
                details.Extensions[name] = value;
            }
        }

        return details;
    }

    private static void RequireNotStarted(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException("The response has already started, so no problem answer can be written.");
        }
    }

    private static Task WriteBodyAsync(HttpResponse response, ProblemDetails body, CancellationToken ct) =>
        response.WriteAsJsonAsync(body, body.GetType(), (JsonSerializerOptions?)null, ContentType, ct);
}
