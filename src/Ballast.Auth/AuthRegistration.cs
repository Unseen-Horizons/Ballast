// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.Auth;

/// <summary>Registers the sign-in library in an app: Identity, the session services, the access check and the JWT bearer scheme.</summary>
public static class AuthRegistration
{
    /// <summary>
    /// Reads and checks the options, then registers the sign-in library over the app's Identity user, roles, database context and refresh
    /// token entity. Call it where the app called <c>AddIdentity</c> before, and register an <see cref="IAuthAudit"/> beside it.
    /// </summary>
    /// <typeparam name="TUser">The app's Identity user. It implements <see cref="IAuthProfile"/> and has a parameterless constructor.</typeparam>
    /// <typeparam name="TKey">The key type: <c>string</c> for a GUID, or <c>int</c>.</typeparam>
    /// <typeparam name="TRole">The app's Identity role, keyed by <typeparamref name="TKey"/>.</typeparam>
    /// <typeparam name="TContext">The app's database context.</typeparam>
    /// <typeparam name="TRefresh">The app's refresh token entity, derived from <see cref="AuthRefreshTokenBase{TKey,TUser}"/>.</typeparam>
    /// <param name="services">The services to add to.</param>
    /// <param name="configuration">The configuration to read the token settings from.</param>
    /// <param name="configure">Sets the rest of the options (password rules, claim names, refusals, sign-in mode). Null for the defaults.</param>
    /// <param name="jwtSection">The section with the token settings. Default <c>Jwt</c>.</param>
    /// <returns>The authentication builder, so the app can add its own schemes (such as Microsoft's) beside the JWT scheme.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A setting is missing or out of range. See <see cref="AuthOptions.Validate"/>.</exception>
    public static AuthenticationBuilder AddBallastAuth<TUser, TKey, TRole, TContext, TRefresh>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AuthOptions>? configure = null,
        string jwtSection = "Jwt")
        where TUser : IdentityUser<TKey>, IAuthProfile, new()
        where TKey : IEquatable<TKey>
        where TRole : IdentityRole<TKey>
        where TContext : DbContext
        where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtSection);

        var options = new AuthOptions();
        options.ReadJwt(configuration.GetSection(jwtSection));
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        // Identity mints its own tokens with the data protection key ring; the app persists the ring where it keeps it.
        services.AddDataProtection();

        // Registered before AddIdentity, which adds Identity's own hasher only when none is registered. The library's hasher reads
        // Identity's format, refuses a hash it cannot read, and reads bcrypt only when the options say so.
        services.AddOptions();
        services.AddScoped<IPasswordHasher<TUser>, AuthPasswordHasher<TUser>>();

        services.AddIdentity<TUser, TRole>(identity => AuthIdentityRules.Apply(identity, options))
            .AddEntityFrameworkStores<TContext>()
            .AddDefaultTokenProviders();

        // With the switch off, Identity checks no address, so the one rule left (no two accounts share one) is checked here.
        if (!options.RequireUniqueEmail)
        {
            services.AddScoped<IUserValidator<TUser>, AddressUniquenessValidator<TUser, TKey>>();
        }

        services.TryAddScoped<IAuthAccountStore, IdentityAuthAccountStore<TUser, TKey>>();
        services.TryAddScoped<IAccessTokenClaims<TUser>, NoAccessTokenClaims<TUser>>();
        services.AddScoped<SessionTokens<TUser, TKey>>();
        services.AddScoped<AccountAccessTokens>();
        services.AddScoped<AuthSessionService<TUser, TKey, TRefresh, TContext>>();
        services.AddScoped<PasswordSignInService<TUser, TKey, TContext>>();
        services.AddScoped<AuthAccountService<TUser, TKey, TRefresh, TContext>>();

        // The JWT bearer is the default scheme. The app may add its own schemes to the builder returned here.
        return services.AddAuthentication(authentication =>
            {
                authentication.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                authentication.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwt => ConfigureJwtBearer(jwt, options));
    }

    private static void ConfigureJwtBearer(JwtBearerOptions jwt, AuthOptions options)
    {
        // The standard claim names stay as the token carries them. Mapping them to the .NET long names would change every claim the app reads.
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            // The default is five minutes, which stretched every 15-minute access token to about twenty.
            ClockSkew = options.ClockSkew,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            NameClaimType = options.Claims.GivenName,
            RoleClaimType = options.Claims.Role,
        };

        // The instant cut-off: a token that validates is still refused when its account has changed (its stamp, a lockout or a deactivation).
        jwt.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var store = context.HttpContext.RequestServices.GetRequiredService<IAuthAccountStore>();
                if (!await AuthAccessCheck.AcceptsAsync(context.Principal, options, store, context.HttpContext.RequestAborted))
                {
                    context.Fail("The session is no longer valid.");
                }
            },
        };
    }
}

/// <summary>
/// The Identity options the sign-in library sets from <see cref="AuthOptions"/>: the password rules, the unique address and the lockout.
/// Identity's own defaults are not kept; the options are the only source.
/// </summary>
internal static class AuthIdentityRules
{
    /// <summary>Copies the password and lockout rules into Identity's options.</summary>
    /// <param name="identity">Identity's options, being built.</param>
    /// <param name="options">The library's options.</param>
    public static void Apply(IdentityOptions identity, AuthOptions options)
    {
        identity.Password.RequiredLength = options.Password.RequiredLength;
        identity.Password.RequireDigit = options.Password.RequireDigit;
        identity.Password.RequireUppercase = options.Password.RequireUppercase;
        identity.Password.RequireLowercase = options.Password.RequireLowercase;
        identity.Password.RequireNonAlphanumeric = options.Password.RequireNonAlphanumeric;
        identity.Password.RequiredUniqueChars = options.Password.RequiredUniqueChars;

        identity.User.RequireUniqueEmail = options.RequireUniqueEmail;

        identity.Lockout.MaxFailedAccessAttempts = options.Lockout.MaxFailedAccessAttempts;
        identity.Lockout.DefaultLockoutTimeSpan = options.Lockout.LockoutDuration;
        identity.Lockout.AllowedForNewUsers = options.Lockout.AllowedForNewUsers;
    }
}
