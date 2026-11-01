// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ballast.AspNetCore;

/// <summary>Registers the problem answers.</summary>
public static class ProblemServiceExtensions
{
    /// <summary>
    /// Registers the problem answers: the options, the default <see cref="IProblemTitles"/> (a registration already made is kept),
    /// the framework's problem details customised for the <c>traceId</c>, the <c>code</c> and the validation title, and the
    /// exception handler. Call <see cref="ProblemApplicationExtensions.UseProblemResponses"/> in the pipeline.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">Sets the options, or null for the defaults.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// An exception mapping is not valid: its title is blank, its Retry-After is below 1, or the list holds a null entry. The message
    /// names the mapping by its position and code.
    /// </exception>
    public static IServiceCollection AddProblemResponses(this IServiceCollection services, Action<ProblemResponseOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The options are read now, so an invalid mapping stops the start rather than the first failed request.
        var options = new ProblemResponseOptions();
        configure?.Invoke(options);
        ValidateMappings(options.ExceptionMappings);

        services.AddSingleton(options);
        services.TryAddSingleton<IProblemTitles, EnglishProblemTitles>();

        services.AddProblemDetails(details => details.CustomizeProblemDetails = context =>
        {
            var http = context.HttpContext;
            var traceId = ProblemResponses.TraceIdOf(http);

            // The framework's validation answer gets the trace reference and the code, in that order, as it always has.
            context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
            if (context.ProblemDetails is ValidationProblemDetails validation)
            {
                validation.Title = ProblemResponses.TitlesOf(http).Title(validation.Status ?? 400, ErrorCodes.ValidationFailed);
                if (!validation.Extensions.ContainsKey("code"))
                {
                    validation.Extensions["code"] = ErrorCodes.ValidationFailed;
                }
            }

            if (!http.Response.HasStarted)
            {
                http.Response.Headers[ProblemResponses.TraceIdHeader] = traceId;
            }
        });

        services.AddExceptionHandler<ProblemExceptionHandler>();
        return services;
    }

    private static void ValidateMappings(IList<ExceptionMapping> mappings)
    {
        for (var index = 0; index < mappings.Count; index++)
        {
            var mapping = mappings[index];
            if (mapping is null)
            {
                throw new InvalidOperationException($"Exception mapping {index + 1} is null. Remove it or give it a rule.");
            }

            if (mapping.Title is not null && string.IsNullOrWhiteSpace(mapping.Title))
            {
                throw new InvalidOperationException($"Exception mapping {index + 1} ({mapping.Code}) has a blank Title. Leave Title null to use the title for its status and code.");
            }

            if (mapping.RetryAfterSeconds is { } seconds && seconds < 1)
            {
                throw new InvalidOperationException($"Exception mapping {index + 1} ({mapping.Code}) has RetryAfterSeconds {seconds}. It must be at least 1, or null for no Retry-After header.");
            }
        }
    }
}
