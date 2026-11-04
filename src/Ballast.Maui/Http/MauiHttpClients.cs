// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Maui.Http;

/// <summary>
/// Builds the app's HTTP clients the same way everywhere: one primary handler rule, one handler lifetime, and the certificate
/// option applied in one place.
/// </summary>
public static class MauiHttpClients
{
    /// <summary>
    /// A client for a singleton that owns its own handler (an auth client, for example), with a base address and a timeout.
    /// </summary>
    /// <param name="baseAddress">The server's base address. Must be absolute.</param>
    /// <param name="timeout">The request timeout.</param>
    /// <param name="options">How the client is built.</param>
    /// <param name="build">Whether this build is debuggable.</param>
    /// <returns>The client. The caller owns it and keeps one for the life of the app.</returns>
    /// <exception cref="InvalidOperationException">The certificate option is on and the build is not debuggable.</exception>
    public static HttpClient Create(Uri baseAddress, TimeSpan timeout, MauiHttpOptions options, IRuntimeBuild build)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(options);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be positive.");

        return new HttpClient(DangerousCertificates.CreateHandler(options.AllowDangerousCertificates, build))
        {
            BaseAddress = baseAddress,
            Timeout = timeout
        };
    }

    /// <summary>
    /// Registers a named client in the service collection, with its handler lifetime and its primary handler, and checks the
    /// certificate option now, so a refused setting stops the start-up rather than the first request.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client's name, for <c>IHttpClientFactory.CreateClient</c>.</param>
    /// <param name="configure">Sets the base address and the timeout on each client the factory makes.</param>
    /// <param name="options">How the client is built.</param>
    /// <param name="build">Whether this build is debuggable.</param>
    /// <returns>The builder, so the app can add its own message handlers.</returns>
    /// <exception cref="InvalidOperationException">The certificate option is on and the build is not debuggable.</exception>
    public static IHttpClientBuilder AddNamedClient(
        this IServiceCollection services,
        string name,
        Action<HttpClient> configure,
        MauiHttpOptions options,
        IRuntimeBuild build)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(build);

        // Checked now: the handler itself is made later, by the factory, when the first request is made.
        DangerousCertificates.CreateHandler(options.AllowDangerousCertificates, build).Dispose();

        var builder = services.AddHttpClient(name, configure)
            .SetHandlerLifetime(options.HandlerLifetime);

        if (options.AllowDangerousCertificates)
            builder.ConfigurePrimaryHttpMessageHandler(() => DangerousCertificates.CreateHandler(true, build));

        return builder;
    }
}
