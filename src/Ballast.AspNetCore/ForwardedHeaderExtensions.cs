// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using IPNetwork = System.Net.IPNetwork;

namespace Ballast.AspNetCore;

/// <summary>
/// The options of the forwarded headers. Set once, in <see cref="ForwardedHeaderExtensions.AddForwardedHeadersFromNetworks"/>.
/// </summary>
public sealed class ForwardedNetworkOptions
{
    /// <summary>
    /// The configuration key of the list of networks, as a list of CIDR strings. The default is <c>ForwardedHeaders:KnownNetworks</c>.
    /// </summary>
    public string ConfigurationKey { get; set; } = "ForwardedHeaders:KnownNetworks";

    /// <summary>
    /// What to do when the list is empty. False, the default, reads no forwarded header, so a caller's X-Forwarded-For cannot choose its
    /// address or its rate-limit bucket. True reads the forwarded headers from any sender, once the framework's loopback-only default
    /// is cleared. Set it only where every request reaches the application through a proxy that sets the headers, and where the
    /// configured list is not yet filled in.
    /// </summary>
    public bool TrustAnySenderWhenEmpty { get; set; }
}

/// <summary>Reads the networks of the proxies that may set the forwarded headers.</summary>
public static class ForwardedHeaderExtensions
{
    /// <summary>
    /// Configures the forwarded headers so that only a hop from one of the configured networks is believed, and reads the list now. The
    /// application still calls <c>UseForwardedHeaders</c> in its pipeline, before anything that reads the client's address.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configuration">The configuration that holds the list.</param>
    /// <param name="configure">Sets the options, or null for the defaults.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// An entry is blank or is not a network in CIDR form, such as <c>10.0.0.0/8</c> or <c>fd00::/8</c>. The message names the entry by its
    /// position and value, and the entry is refused, never skipped.
    /// </exception>
    public static IServiceCollection AddForwardedHeadersFromNetworks(this IServiceCollection services, IConfiguration configuration, Action<ForwardedNetworkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new ForwardedNetworkOptions();
        configure?.Invoke(options);

        List<IPNetwork> networks = [];
        var entries = configuration.GetSection(options.ConfigurationKey).GetChildren().ToArray();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index].Value;
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw new InvalidOperationException($"{options.ConfigurationKey} entry {index} is blank. Remove it, or give it a network in CIDR form.");
            }

            if (!IPNetwork.TryParse(entry, out var network))
            {
                throw new InvalidOperationException($"{options.ConfigurationKey} entry {index} ('{entry}') is not a network in CIDR form, such as 10.0.0.0/8 or fd00::/8. It is refused, not skipped.");
            }

            networks.Add(network);
        }

        var trustAny = options.TrustAnySenderWhenEmpty;
        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            // The forwarded headers are read only from a listed network. With an empty list and no opt-in, none is read at all.
            forwarded.ForwardedHeaders = networks.Count > 0 || trustAny
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                : ForwardedHeaders.None;
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            foreach (var network in networks)
            {
                forwarded.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }
}
