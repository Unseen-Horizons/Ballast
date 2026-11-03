// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ballast.Auth;

/// <summary>Registers the single-use e-mail sign-in link beside the sign-in library.</summary>
public static class AuthEmailLinkRegistration
{
    /// <summary>
    /// Reads and checks the link settings, then registers <see cref="EmailLinkService{TContext}"/>. Call it after
    /// <see cref="AuthRegistration.AddBallastAuth{TUser,TKey,TRole,TContext,TRefresh}"/> (which provides the refusals) and after the app's
    /// mail registration (which provides the <c>EmailService</c>).
    /// </summary>
    /// <typeparam name="TContext">The app's database context, which maps the link table.</typeparam>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">Sets the lifetime and the rate limit. Null for the defaults (15 minutes, 5 links an hour).</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A setting is out of range. See <see cref="AuthEmailLinkOptions.Validate"/>.</exception>
    public static IServiceCollection AddBallastEmailLinks<TContext>(this IServiceCollection services, Action<AuthEmailLinkOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AuthEmailLinkOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<EmailLinkService<TContext>>();
        return services;
    }
}
