// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.AspNetCore;

/// <summary>Registers the security headers, puts them into the pipeline, and applies the static file rules.</summary>
public static class SecurityHeaderServiceExtensions
{
    /// <summary>
    /// Registers the security headers and their static file rules. The options are read now, so a blank header value or a blank
    /// header name stops the start.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">Sets the options, or null for the defaults.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A header value is blank, or a static file rule is null.</exception>
    public static IServiceCollection AddSecurityHeaders(this IServiceCollection services, Action<SecurityHeaderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SecurityHeaderOptions();
        configure?.Invoke(options);
        RequireValue(options.ReferrerPolicy, nameof(options.ReferrerPolicy));
        RequireValue(options.FrameOptions, nameof(options.FrameOptions));
        if (options.PermissionsPolicy is not null)
        {
            RequireValue(options.PermissionsPolicy, nameof(options.PermissionsPolicy));
        }

        RequireValue(options.DefaultContentSecurityPolicy, nameof(options.DefaultContentSecurityPolicy));
        foreach (var rule in options.StaticFileRules)
        {
            if (rule is null)
            {
                throw new InvalidOperationException("A static file rule is null. Remove it or give it a path.");
            }

            if (rule.ContentSecurityPolicy is not null)
            {
                RequireValue(rule.ContentSecurityPolicy, $"the content security policy of the rule for '{rule.Path}'");
            }

            if (rule.CacheControl is not null)
            {
                RequireValue(rule.CacheControl, $"the cache control of the rule for '{rule.Path}'");
            }

            foreach (var (name, value) in rule.Headers)
            {
                RequireValue(name, $"a header name in the rule for '{rule.Path}'");
                RequireValue(value, $"the header '{name}' in the rule for '{rule.Path}'");
            }
        }

        services.AddSingleton(options);
        return services;
    }

    /// <summary>
    /// Writes the security headers of every response before the rest of the pipeline runs: <c>X-Content-Type-Options</c>, the referrer
    /// policy, the frame options, the permissions policy and, unless the response already has one, the default content security policy.
    /// A later component may overwrite any of them.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="AddSecurityHeaders"/> was not called.</exception>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = RequireOptions(app);

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = options.ReferrerPolicy;
            headers["X-Frame-Options"] = options.FrameOptions;
            if (options.PermissionsPolicy is not null)
            {
                headers["Permissions-Policy"] = options.PermissionsPolicy;
            }

            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers["Content-Security-Policy"] = options.DefaultContentSecurityPolicy;
            }

            await next();
        });
    }

    /// <summary>
    /// Serves the static files of the web root with the static file rules applied to each one. The rules are the ones registered in
    /// <see cref="AddSecurityHeaders"/>, read now. A handler the caller sets in <paramref name="configure"/> still runs, after the rules.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="configure">Sets the static file options, or null for the defaults.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="AddSecurityHeaders"/> was not called.</exception>
    public static IApplicationBuilder UseSecureStaticFiles(this IApplicationBuilder app, Action<StaticFileOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var rules = RequireOptions(app).StaticFileRules.ToArray();

        var options = new StaticFileOptions();
        configure?.Invoke(options);
        var previous = options.OnPrepareResponse;
        options.OnPrepareResponse = context =>
        {
            ApplyRules(context, rules);
            previous?.Invoke(context);
        };

        return app.UseStaticFiles(options);
    }

    private static void ApplyRules(StaticFileResponseContext context, IReadOnlyList<StaticFileRule> rules)
    {
        var request = context.Context.Request;
        var headers = context.Context.Response.Headers;
        foreach (var rule in rules)
        {
            if (!rule.Matches(request.Path))
            {
                continue;
            }

            if (rule.ContentSecurityPolicy is not null)
            {
                headers["Content-Security-Policy"] = rule.ContentSecurityPolicy;
            }

            if (rule.CacheControl is not null)
            {
                headers.CacheControl = rule.CacheControl;
            }

            foreach (var (name, value) in rule.Headers)
            {
                headers[name] = value;
            }
        }
    }

    private static SecurityHeaderOptions RequireOptions(IApplicationBuilder app)
    {
        return app.ApplicationServices.GetService<SecurityHeaderOptions>()
            ?? throw new InvalidOperationException("The security headers are not registered. Call AddSecurityHeaders in the service collection before Build().");
    }

    private static void RequireValue(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{what} is blank. Give it a value, or leave the option at its default.");
        }
    }
}
