// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Ballast.Errors;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ballast.AspNetCore;

/// <summary>
/// Answers an exception as a problem. An abandoned request is answered 499 with no body. An <see cref="AppException"/> keeps its
/// own status, code and message. Otherwise the first matching <see cref="ExceptionMapping"/> answers it, and anything else is the
/// internal error, which carries the trace reference and never the exception's text.
/// </summary>
internal sealed class ProblemExceptionHandler : IExceptionHandler
{
    private readonly ProblemResponseOptions _options;
    private readonly ILogger<ProblemExceptionHandler> _logger;

    public ProblemExceptionHandler(ProblemResponseOptions options, ILogger<ProblemExceptionHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = ProblemResponses.TraceIdOf(httpContext);
        var response = httpContext.Response;
        if (!response.HasStarted)
        {
            response.Headers[ProblemResponses.TraceIdHeader] = traceId;
        }

        // The caller went away. Nothing failed and nobody reads an answer, so it is logged at debug and answered with no body.
        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("{Method} {Path} cancelled: the client disconnected (trace {TraceId})", httpContext.Request.Method, httpContext.Request.Path.Value, traceId);
            if (!response.HasStarted)
            {
                response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            return true;
        }

        var answer = Describe(httpContext, exception, traceId);
        Log(httpContext, exception, answer, traceId);

        if (response.HasStarted)
        {
            _logger.LogWarning("The response had already started, so the answer for trace {TraceId} was not written.", traceId);
            return true;
        }

        if (answer.RetryAfterSeconds is { } seconds)
        {
            response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        await ProblemResponses.WriteAsync(httpContext, answer.Problem, cancellationToken);
        return true;
    }

    private Answer Describe(HttpContext httpContext, Exception exception, string traceId)
    {
        var instance = httpContext.Request.Path.Value;

        if (exception is AppException refusal)
        {
            var level = refusal.StatusCode >= 500 ? LogLevel.Error : LogLevel.Information;
            return new Answer(Problem.From(refusal, traceId, instance), level, null, AnswerKind.Refusal);
        }

        var titles = ProblemResponses.TitlesOf(httpContext);
        foreach (var mapping in _options.ExceptionMappings)
        {
            if (!mapping.Matches(exception))
            {
                continue;
            }

            var problem = new Problem
            {
                Type = ProblemTypes.For(mapping.Status),
                Title = mapping.TitleFactory?.Invoke(httpContext, exception) ?? mapping.Title ?? titles.Title(mapping.Status, mapping.Code),
                Status = mapping.Status,
                Detail = mapping.DetailFactory?.Invoke(httpContext, exception),
                Instance = instance,
                Code = mapping.Code,
                TraceId = traceId,
            };
            var level = mapping.LogLevel ?? (mapping.Status >= 500 ? LogLevel.Error : LogLevel.Information);
            return new Answer(problem, level, mapping.RetryAfterSeconds, AnswerKind.Mapped);
        }

        var internalError = Problem.InternalError(traceId, instance) with
        {
            Title = titles.Title(500, ErrorCodes.InternalError),
        };
        return new Answer(internalError, LogLevel.Error, null, AnswerKind.Unhandled);
    }

    private void Log(HttpContext httpContext, Exception exception, Answer answer, string traceId)
    {
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value;
        var logged = answer.Level >= LogLevel.Error ? exception : null;
        var problem = answer.Problem;

        switch (answer.Kind)
        {
            case AnswerKind.Refusal:
                _logger.Log(answer.Level, logged, "{Method} {Path} answered {Status} with {Code}: {Message} (trace {TraceId})",
                    method, path, problem.Status, problem.Code, exception.Message, traceId);
                break;
            case AnswerKind.Mapped:
                _logger.Log(answer.Level, logged, "{Method} {Path} answered {Status} with {Code} for {ExceptionType}: {Message} (trace {TraceId})",
                    method, path, problem.Status, problem.Code, exception.GetType().Name, exception.Message, traceId);
                break;
            default:
                _logger.LogError(exception, "Unhandled exception on {Method} {Path} (trace {TraceId})", method, path, traceId);
                break;
        }
    }

    private enum AnswerKind
    {
        Refusal,
        Mapped,
        Unhandled,
    }

    private sealed record Answer(Problem Problem, LogLevel Level, int? RetryAfterSeconds, AnswerKind Kind);
}
