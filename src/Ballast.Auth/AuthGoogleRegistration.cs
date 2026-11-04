// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ballast.Auth;

/// <summary>Registers Google sign-in beside the sign-in library.</summary>
public static class AuthGoogleRegistration
{
    /// <summary>
    /// Reads the Google settings, then registers the Google validator and <see cref="GoogleSignInService{TUser,TKey,TRefresh,TContext}"/>.
    /// Call it after <see cref="AuthRegistration.AddBallastAuth{TUser,TKey,TRole,TContext,TRefresh}"/>. Register an
    /// <see cref="IAuthNewAccount{TUser}"/> before it when the app keeps rows for a new account (without one, there are none).
    /// The server starts without a Web client id: Google sign-in then answers <see cref="AuthFailures.GoogleSignInNotConfigured"/>.
    /// </summary>
    /// <typeparam name="TUser">The app's Identity user.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TRefresh">The app's refresh token entity.</typeparam>
    /// <typeparam name="TContext">The app's database context.</typeparam>
    /// <param name="services">The services to add to.</param>
    /// <param name="configuration">The configuration to read <c>WebClientId</c> and <c>NewAccountRole</c> from.</param>
    /// <param name="sectionName">The section those keys are in. Default <c>Google</c>.</param>
    /// <param name="configure">Sets the Google options after they are read. Null for none.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="sectionName"/> is null, empty or blank.</exception>
    public static IServiceCollection AddBallastGoogleSignIn<TUser, TKey, TRefresh, TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Google",
        Action<AuthGoogleOptions>? configure = null)
        where TUser : IdentityUser<TKey>, IAuthProfile, new()
        where TKey : IEquatable<TKey>
        where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var options = new AuthGoogleOptions
        {
            WebClientId = configuration[$"{sectionName}:WebClientId"],
            NewAccountRole = configuration[$"{sectionName}:NewAccountRole"],
        };
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IGoogleTokenVerifier, GoogleJsonWebSignatureVerifier>();
        services.TryAddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
        services.TryAddScoped<IAuthNewAccount<TUser>, NoAuthNewAccount<TUser>>();
        services.AddScoped<GoogleSignInService<TUser, TKey, TRefresh, TContext>>();
        return services;
    }
}
