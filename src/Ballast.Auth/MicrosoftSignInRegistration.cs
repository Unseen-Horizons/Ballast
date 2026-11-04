// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Ballast.Auth;

/// <summary>
/// Registers Microsoft staff sign-in. The settings are read and checked at start. The services are always registered; the OpenID Connect and
/// cookie schemes are added only when the tenant and the client are set, so an app without Microsoft sign-in has no Microsoft scheme at all.
/// The app must also register the job lock (Ballast.Jobs), because the directory sync requires <see cref="Ballast.Jobs.IJobLeadership"/>.
/// </summary>
public static class MicrosoftSignInRegistration
{
    /// <summary>
    /// Registers the Microsoft services and, when enabled, the handshake schemes, and maps nothing: call
    /// <see cref="MicrosoftSignInEndpoints.MapBallastMicrosoftSignIn{TUser,TKey,TRole,TRefresh,TContext}"/> to add the routes.
    /// </summary>
    /// <typeparam name="TUser">The app's Identity user.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TRole">The app's Identity role.</typeparam>
    /// <typeparam name="TRefresh">The app's refresh token entity.</typeparam>
    /// <typeparam name="TContext">The app's database context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration the settings are read from.</param>
    /// <param name="section">The section name. Default <c>MicrosoftSignIn</c>.</param>
    /// <param name="configure">Sets the rest of the options after they are read. Null for none.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException">A setting cannot be used. See <see cref="MicrosoftSignInOptions.Validate"/>.</exception>
    public static IServiceCollection AddBallastMicrosoftSignIn<TUser, TKey, TRole, TRefresh, TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string section = "MicrosoftSignIn",
        Action<MicrosoftSignInOptions>? configure = null)
        where TUser : Microsoft.AspNetCore.Identity.IdentityUser<TKey>, IAuthProfile, new()
        where TKey : IEquatable<TKey>
        where TRole : Microsoft.AspNetCore.Identity.IdentityRole<TKey>, new()
        where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
        where TContext : Microsoft.EntityFrameworkCore.DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var microsoft = MicrosoftSignInOptions.Read(configuration, section, configure);
        services.AddSingleton(microsoft);
        services.AddScoped<MicrosoftSignInService<TUser, TKey, TRole, TRefresh, TContext>>();
        services.AddScoped<MicrosoftDirectorySync<TUser, TKey, TRole, TRefresh, TContext>>();

        // The app's emergency accounts. The default names none; an app registers its own, and that registration wins.
        services.TryAddScoped<IMicrosoftEmergencyAccounts<TKey>, NoMicrosoftEmergencyAccounts<TKey>>();

        // The app's own profile of a staff member. The default keeps none; an app registers its own, and that registration is used.
        services.TryAddScoped<IMicrosoftStaffProfile<TUser, TKey>, NoMicrosoftStaffProfile<TUser, TKey>>();

        if (microsoft.SyncEnabled)
        {
            services.AddHttpClient<IMicrosoftGraphClient, MicrosoftGraphClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
        }

        if (microsoft.IsEnabled)
        {
            services.AddAuthentication()
                .AddCookie(microsoft.CookieScheme, options =>
                {
                    // Carries the handshake result to the complete step, for five minutes at most. Not sliding, and not for the API.
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                    options.SlidingExpiration = false;
                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                })
                .AddOpenIdConnect(microsoft.Scheme, options => ConfigureOpenIdConnect(options, microsoft));
        }

        return services;
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions options, MicrosoftSignInOptions microsoft)
    {
        options.Authority = microsoft.Authority;
        options.ClientId = microsoft.ClientId;

        // The ID token comes back by form post, and the handler checks its signature, issuer, audience, lifetime and nonce. The browser flow has
        // no client secret: the secret is used only by the directory sync.
        options.ResponseType = "id_token";
        options.ResponseMode = "form_post";
        options.CallbackPath = microsoft.CallbackPath;

        // The handshake result goes to the cookie scheme, not the application's own sign-in.
        options.SignInScheme = microsoft.CookieScheme;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        // Microsoft's own claim names (oid, tid, roles) are read as they are, not mapped to the long XML claim types.
        options.MapInboundClaims = false;
        options.SaveTokens = false;
        options.TokenValidationParameters.ValidIssuer = microsoft.Authority;

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                if (context.Properties.Items.TryGetValue(MicrosoftSignInEndpoints.LoginHintItem, out var hint) && !string.IsNullOrEmpty(hint))
                {
                    context.ProtocolMessage.LoginHint = hint;
                }

                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                // The failure message can carry Microsoft's text, so the log gets the failure type and Microsoft's own code only. The browser
                // gets the same code, never the message.
                var message = context.Failure?.Message ?? string.Empty;
                var oauthError = context.Request.HasFormContentType ? context.Request.Form["error"].ToString() : null;
                var providerCode = MicrosoftSignInRedirects.ProviderCode(message, oauthError);
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Ballast.Auth.MicrosoftSignIn")
                    .LogWarning(
                        "Microsoft sign-in failed ({Reason}, Microsoft code {ProviderCode}).",
                        context.Failure?.GetType().Name ?? "unknown",
                        providerCode ?? "none");
                context.HandleResponse();
                context.Response.Redirect(MicrosoftSignInRedirects.Error(microsoft.LoginPagePath, MicrosoftRefusal.SignInFailed, providerCode));
                return Task.CompletedTask;
            },
        };
    }
}
