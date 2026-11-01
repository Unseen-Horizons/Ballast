// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Http;

/// <summary>Registers a <see cref="PoliteFetcher"/> and the named client it reads through.</summary>
public static class PoliteFetcherExtensions
{
    /// <summary>
    /// Registers a <see cref="PoliteFetcher"/> under <paramref name="name"/> (a keyed singleton, so one app can run more than one bot
    /// identity), and the named client it reads through. The client is made by <see cref="SafeHttpClientExtensions.AddSafeHttpClient"/>:
    /// no automatic redirects, the public-only connect check, the User-Agent <c>{ProductToken}/{Version} (+{BotInfoUrl})</c>, and the
    /// timeout from the options.
    /// </summary>
    /// <param name="services">The services to register into. Required.</param>
    /// <param name="name">The client's and the fetcher's key, for example <c>discovery</c>. Required.</param>
    /// <param name="configure">Sets the product token, the bot-info URL and the other options. Required.</param>
    /// <returns>The same services.</returns>
    /// <exception cref="ArgumentException">
    /// The product token, the version or the bot-info URL is not valid, a blocked host is not a bare host name, or the timeout is not above zero.
    /// </exception>
    public static IServiceCollection AddPoliteFetcher(
        this IServiceCollection services, string name, Action<PoliteFetcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PoliteFetcherOptions();
        configure(options);
        Validate(options);

        var userAgent = $"{options.ProductToken}/{options.Version} (+{options.BotInfoUrl})";
        services.AddSafeHttpClient(name, client =>
        {
            client.UserAgent = userAgent;
            client.Timeout = options.Timeout;
        });

        services.AddKeyedSingleton<PoliteFetcher>(name, (sp, _) => new PoliteFetcher(
            sp.GetRequiredService<IHttpClientFactory>(),
            name,
            options,
            sp.GetService<TimeProvider>()));
        return services;
    }

    /// <summary>Refuses an options value that would name the crawler wrongly, or that would block nothing. Used at registration and by the fetcher.</summary>
    internal static void Validate(PoliteFetcherOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProductToken) || !options.ProductToken.All(IsTokenCharacter))
        {
            throw new ArgumentException(
                "PoliteFetcherOptions.ProductToken is required and holds only letters, digits, '-', '_' and '.', for example ExampleBot.",
                nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Version) || !new HttpRequestMessage().Headers.UserAgent.TryParseAdd($"{options.ProductToken}/{options.Version}"))
        {
            throw new ArgumentException("PoliteFetcherOptions.Version is required and must be a valid version, for example 1.0.", nameof(options));
        }

        if (!Uri.TryCreate(options.BotInfoUrl, UriKind.Absolute, out var info) || info.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "PoliteFetcherOptions.BotInfoUrl must be an absolute https URL, so that a site can read what the crawler does.", nameof(options));
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("PoliteFetcherOptions.Timeout must be above zero.", nameof(options));
        }

        if (options.MaxCrawlDelay < TimeSpan.Zero)
        {
            throw new ArgumentException("PoliteFetcherOptions.MaxCrawlDelay cannot be below zero.", nameof(options));
        }

        foreach (var host in options.BlockedHosts)
        {
            // An entry that is not a bare host name (a URL, say) would block nothing and fail silently, so it is refused here.
            if (string.IsNullOrWhiteSpace(host) || host.Contains('/') || host.Contains(':') || host.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException($"A blocked host must be a bare host name, such as example.test, not '{host}'.", nameof(options));
            }
        }
    }

    private static bool IsTokenCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';
}
