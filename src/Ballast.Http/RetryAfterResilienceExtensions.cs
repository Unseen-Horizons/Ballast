// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

using Polly;
using Polly.Retry;

namespace Ballast.Http;

/// <summary>Adds a retry that honours the server's Retry-After, to a named client.</summary>
public static class RetryAfterResilienceExtensions
{
    /// <summary>The name of the resilience handler this adds to the client.</summary>
    public const string HandlerName = "Ballast.RetryAfter";

    /// <summary>
    /// Adds a retry to the client's pipeline. A status in <see cref="RetryAfterOptions.RetryStatuses"/> (429, 502, 503 and 504 by
    /// default) or a connection failure is tried again, up to <see cref="RetryAfterOptions.MaxAttempts"/> tries in all. Each retry
    /// waits for the answer's Retry-After (capped at <see cref="RetryAfterOptions.MaxRetryAfter"/>), or the configured delay for that
    /// retry when the answer gives none. A safety refusal, a timeout and any other status are the answer, at once. The last
    /// response is returned as it is, so the caller still sees the status.
    /// </summary>
    /// <param name="builder">The client to add the retry to. Required.</param>
    /// <param name="configure">Sets the options. Null uses the defaults.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="RetryAfterOptions.MaxAttempts"/> is below 1, or <see cref="RetryAfterOptions.MaxRetryAfter"/> is below zero.
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="RetryAfterOptions.DefaultDelays"/> is empty or holds a negative delay.</exception>
    public static IHttpClientBuilder AddRetryAfterResilience(this IHttpClientBuilder builder, Action<RetryAfterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new RetryAfterOptions();
        configure?.Invoke(options);
        Validate(options);
        if (options.MaxAttempts == 1)
        {
            return builder;
        }

        builder.AddResilienceHandler(HandlerName, (pipeline, _) => pipeline.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = options.MaxAttempts - 1,
            BackoffType = DelayBackoffType.Constant,
            UseJitter = false,
            ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome, options)),
            DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(DelayFor(args.Outcome, args.AttemptNumber, options)),
            // The response of a try that is tried again is not returned to anyone, so its connection is released here.
            OnRetry = args =>
            {
                args.Outcome.Result?.Dispose();
                return default;
            },
        }));

        return builder;
    }

    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, RetryAfterOptions options)
    {
        if (outcome.Exception is HttpRequestException failure)
        {
            return options.RetryOnConnectionFailure && failure.InnerException is not HttpSafetyException;
        }

        return outcome.Result is { } response && options.RetryStatuses.Contains(response.StatusCode);
    }

    private static TimeSpan DelayFor(Outcome<HttpResponseMessage> outcome, int retryNumber, RetryAfterOptions options)
    {
        if (outcome.Result is { } response && RetryAfter.Read(response, options.MaxRetryAfter) is { } asked)
        {
            return asked;
        }

        var index = Math.Min(Math.Max(retryNumber, 0), options.DefaultDelays.Count - 1);
        return options.DefaultDelays[index];
    }

    private static void Validate(RetryAfterOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRetryAfter, TimeSpan.Zero);
        if (options.DefaultDelays.Count == 0)
        {
            throw new ArgumentException("RetryAfterOptions.DefaultDelays needs at least one delay.", nameof(options));
        }
        if (options.DefaultDelays.Any(delay => delay < TimeSpan.Zero))
        {
            throw new ArgumentException("A retry delay cannot be below zero.", nameof(options));
        }
    }
}
