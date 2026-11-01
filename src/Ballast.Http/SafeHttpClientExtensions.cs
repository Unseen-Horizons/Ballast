// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Http;

/// <summary>Registers the named client that outbound calls use.</summary>
public static class SafeHttpClientExtensions
{
    /// <summary>
    /// Registers a named <see cref="HttpClient"/> for outbound calls: automatic redirects are off (so <see cref="RedirectFollower"/>
    /// decides each hop), the connect-time public-address check is installed, and the client sends the app's User-Agent and has the
    /// given timeout. A host may still configure the handler after this call.
    /// </summary>
    /// <param name="services">The services to register into. Required.</param>
    /// <param name="name">The client's name, which <see cref="RedirectFollower"/> and <see cref="PoliteFetcherExtensions"/> use. Required.</param>
    /// <param name="configure">Sets <see cref="SafeHttpClientOptions.UserAgent"/> and the other options. Required.</param>
    /// <returns>The client builder, so a resilience handler can be added with <see cref="RetryAfterResilienceExtensions.AddRetryAfterResilience"/>.</returns>
    /// <exception cref="ArgumentException">
    /// The name is blank, the user agent is blank or not a valid product, or the timeout is not above zero.
    /// </exception>
    public static IHttpClientBuilder AddSafeHttpClient(
        this IServiceCollection services, string name, Action<SafeHttpClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SafeHttpClientOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.UserAgent))
        {
            throw new ArgumentException("SafeHttpClientOptions.UserAgent is required: name the app in the User-Agent, for example ExampleApp/1.0.", nameof(configure));
        }
        if (!new HttpRequestMessage().Headers.UserAgent.TryParseAdd(options.UserAgent))
        {
            throw new ArgumentException($"SafeHttpClientOptions.UserAgent '{options.UserAgent}' is not a valid product token with an optional comment.", nameof(configure));
        }
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("SafeHttpClientOptions.Timeout must be above zero.", nameof(configure));
        }

        var userAgent = options.UserAgent;
        var timeout = options.Timeout;
        var allowNonPublic = options.AllowNonPublicAddresses;
        return services.AddHttpClient(name, client =>
            {
                client.Timeout = timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = allowNonPublic ? null : PublicDestinationGuard.ConnectToPublicOnlyAsync,
            });
    }
}
