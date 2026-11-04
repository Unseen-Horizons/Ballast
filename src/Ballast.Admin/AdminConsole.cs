// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AspNetCore;

namespace Ballast.Admin;

/// <summary>
/// The headers of a staff console's static pages: a content security policy that allows no inline script and no CDN, a
/// cache rule that checks the file before it is used, and the headers that keep the console out of search results and out of
/// the referrer.
/// </summary>
public static class AdminConsole
{
    /// <summary>
    /// The static file rule of a console, built from its options. The rule is ready to add to
    /// <see cref="SecurityHeaderOptions.StaticFileRules"/>, or use <see cref="AddAdminConsole"/>.
    /// </summary>
    /// <param name="configure">Sets the options, or null for the defaults.</param>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentException">
    /// The path is blank or does not start with '/', the cache control is blank, or a source is not valid (see
    /// <see cref="ContentSecurityPolicy"/>).
    /// </exception>
    public static StaticFileRule Rule(Action<AdminConsoleOptions>? configure = null)
    {
        var options = new AdminConsoleOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CacheControl, nameof(AdminConsoleOptions.CacheControl));

        var rule = new StaticFileRule(options.Path)
        {
            CacheControl = options.CacheControl,
            ContentSecurityPolicy = ContentSecurityPolicy(options),
        };
        rule.Headers["X-Robots-Tag"] = "noindex, nofollow";
        rule.Headers["X-Content-Type-Options"] = "nosniff";
        rule.Headers["Referrer-Policy"] = "no-referrer";
        return rule;
    }

    /// <summary>
    /// Adds the console's static file rule to a set of security header options. A later rule for the same files overwrites the
    /// headers this one sets, so add the console's rule before any page-specific rule that should win.
    /// </summary>
    /// <param name="options">The options to add to. Required.</param>
    /// <param name="configure">Sets the console's options, or null for the defaults.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The rule's options are not valid (see <see cref="Rule"/>).</exception>
    public static SecurityHeaderOptions AddAdminConsole(this SecurityHeaderOptions options, Action<AdminConsoleOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.StaticFileRules.Add(Rule(configure));
        return options;
    }

    /// <summary>
    /// The <c>Content-Security-Policy</c> header of the console. Scripts come only from the console's own origin unless the options name
    /// more (<see cref="AdminConsoleOptions.ScriptSources"/>, or inline scripts with <see cref="AdminConsoleOptions.AllowInlineScripts"/>),
    /// and the page cannot be framed, change its base address or post a form anywhere but its own origin.
    /// </summary>
    /// <param name="options">The options. Required.</param>
    /// <returns>The policy, in the order the directives are written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A source is blank, holds white space, a ';' or a ',', or is <c>*</c>, or a script source is <c>'unsafe-inline'</c> or
    /// <c>'unsafe-eval'</c>. A source that could end a directive early, or open every origin, is refused rather than written.
    /// </exception>
    public static string ContentSecurityPolicy(AdminConsoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var script = new List<string> { "'self'" };
        if (options.AllowInlineScripts)
        {
            script.Add("'unsafe-inline'");
        }

        // Inline script and eval are reached only through AllowInlineScripts (eval never), so that flag is the one visible switch.
        RefuseInlineKeywords(options.ScriptSources, nameof(AdminConsoleOptions.ScriptSources));

        script.AddRange(Sources(options.ScriptSources, nameof(AdminConsoleOptions.ScriptSources)));

        var style = new List<string> { "'self'" };
        if (options.AllowInlineStyles)
        {
            style.Add("'unsafe-inline'");
        }

        style.AddRange(Sources(options.StyleSources, nameof(AdminConsoleOptions.StyleSources)));

        var image = new List<string> { "'self'", "data:" };
        image.AddRange(Sources(options.ImageSources, nameof(AdminConsoleOptions.ImageSources)));

        var connect = new List<string> { "'self'" };
        connect.AddRange(Sources(options.ConnectSources, nameof(AdminConsoleOptions.ConnectSources)));

        var font = new List<string> { "'self'" };
        font.AddRange(Sources(options.FontSources, nameof(AdminConsoleOptions.FontSources)));

        List<string> directives =
        [
            "default-src 'self'",
            "script-src " + string.Join(' ', script),
            "style-src " + string.Join(' ', style),
            "img-src " + string.Join(' ', image),
            "connect-src " + string.Join(' ', connect),
            "font-src " + string.Join(' ', font),
        ];

        // worker-src is written only when the console names a worker source. Without it, workers follow script-src.
        var worker = Sources(options.WorkerSources, nameof(AdminConsoleOptions.WorkerSources));
        if (worker.Count > 0)
        {
            directives.Add("worker-src 'self' " + string.Join(' ', worker));
        }

        directives.Add("frame-ancestors 'none'");
        directives.Add("base-uri 'self'");
        directives.Add("form-action 'self'");
        return string.Join("; ", directives);
    }

    private static List<string> Sources(IEnumerable<string> sources, string name)
    {
        var valid = new List<string>();
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)
                || source.Any(char.IsWhiteSpace)
                || source.Contains(';')
                || source.Contains(',')
                || source == "*")
            {
                throw new ArgumentException(
                    $"The source '{source}' in {name} is not valid. A source must not be blank, hold white space, ';' or ',', or be '*'.",
                    name);
            }

            valid.Add(source);
        }

        return valid;
    }

    private static void RefuseInlineKeywords(IEnumerable<string> sources, string name)
    {
        if (sources.Any(source => source is "'unsafe-inline'" or "'unsafe-eval'"))
        {
            throw new ArgumentException(
                "A script source must not be 'unsafe-inline' or 'unsafe-eval'. Inline script is turned on with AllowInlineScripts, and eval is never allowed.",
                name);
        }
    }
}
