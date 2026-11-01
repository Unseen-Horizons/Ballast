// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Email;

/// <summary>Registers the mail services of Ballast.Email.</summary>
public static class EmailServiceCollectionExtensions
{
    /// <summary>The time a send may take before the client gives up and the send is a failure. The app can change it through <c>configureHttpClient</c>.</summary>
    private static readonly TimeSpan _defaultHttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reads and checks the mail settings, then registers <see cref="MailSettings"/> and the scoped <see cref="EmailService"/>.
    /// The SendGrid transport is registered only when an API key is set, with the default timeout of 30 seconds.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configuration">The configuration to read the settings from.</param>
    /// <param name="sectionName">The section to read: <c>SendGrid</c> unless the app names another.</param>
    /// <param name="defaultFromName">The sender name to use when the section has none, or null for none.</param>
    /// <param name="configureHttpClient">Changes the client that sends the mail (its timeout, for example). Null for none.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The settings are inconsistent. See <see cref="MailSettings.Read"/>.</exception>
    /// <remarks>
    /// The service that sends with the registered settings is resolved from the container, so the host's logging and, in
    /// Development, the environment must be registered too. The send-once ledger is not registered here, because it needs the
    /// app's store: register a <see cref="SendOnceLedger"/> beside this call.
    /// </remarks>
    public static IServiceCollection AddBallastEmail(this IServiceCollection services, IConfiguration configuration, string sectionName = MailSettings.DefaultSectionName, string? defaultFromName = null, Action<IHttpClientBuilder>? configureHttpClient = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = MailSettings.Read(configuration, sectionName, defaultFromName);
        services.AddSingleton(settings);
        services.AddScoped(sp => new EmailService(
            sp.GetRequiredService<MailSettings>(),
            sp.GetRequiredService<ILogger<EmailService>>(),
            sp.GetService<IMailTransport>(),
            sp.GetService<IHostEnvironment>()?.IsDevelopment() ?? false));

        if (settings.IsConfigured)
        {
            var builder = services.AddHttpClient<IMailTransport, SendGridMailTransport>(client => client.Timeout = _defaultHttpTimeout);
            configureHttpClient?.Invoke(builder);
        }

        return services;
    }
}
