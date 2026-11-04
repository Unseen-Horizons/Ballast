// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Auth;

/// <summary>Registers <see cref="DynamicClaimsTransformation{TUser,TKey,TRole}"/> in an app.</summary>
public static class DynamicClaimsRegistration
{
    /// <summary>
    /// Adds the claims transformation as the app's <see cref="IClaimsTransformation"/>, with its own memory cache. Call it after
    /// <c>AddBallastAuth</c>, which registers Identity's managers. An app that sets a size limit on its memory cache must register that
    /// cache before this call.
    /// </summary>
    /// <typeparam name="TUser">The app's Identity user.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TRole">The app's Identity role.</typeparam>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">Sets the claim names and cache times. Null for the defaults.</param>
    /// <returns>The same services.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The options are out of range. See <see cref="DynamicClaimsOptions.Validate"/>.</exception>
    public static IServiceCollection AddBallastDynamicClaims<TUser, TKey, TRole>(
        this IServiceCollection services,
        Action<DynamicClaimsOptions>? configure = null)
        where TUser : IdentityUser<TKey>
        where TKey : IEquatable<TKey>
        where TRole : IdentityRole<TKey>
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DynamicClaimsOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddMemoryCache();
        services.AddTransient<IClaimsTransformation>(sp => new DynamicClaimsTransformation<TUser, TKey, TRole>(
            sp.GetRequiredService<UserManager<TUser>>(),
            sp.GetRequiredService<RoleManager<TRole>>(),
            sp.GetRequiredService<IMemoryCache>(),
            options));

        return services;
    }
}
