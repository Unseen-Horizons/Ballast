// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace Ballast.Auth;

/// <summary>How a person's Microsoft app roles become the roles of an account.</summary>
public enum MicrosoftRoleMode
{
    /// <summary>Every role value that counts for this console is a role of the account. The default.</summary>
    Many,

    /// <summary>The person has exactly one console role. None is refused at sign-in as no role; several are refused, and the sync leaves them out.</summary>
    One,
}

/// <summary>
/// The settings of Microsoft staff sign-in: the Entra app registration (tenant and client), the client secret the directory sync uses, the
/// company domains, how the role values are read, and the paths of the sign-in page and its callback. Read from the <c>MicrosoftSignIn</c>
/// section by <see cref="Read"/>. The defaults are the paths of the first apps that use it.
/// </summary>
public sealed class MicrosoftSignInOptions
{
    /// <summary>The section the settings were read from. Used in the messages of <see cref="Validate"/>.</summary>
    public string Section { get; set; } = "MicrosoftSignIn";

    /// <summary>The Entra tenant (directory) id, a GUID. Empty turns Microsoft sign-in off.</summary>
    public string? TenantId { get; set; }

    /// <summary>The Entra application (client) id, a GUID. Empty turns Microsoft sign-in off.</summary>
    public string? ClientId { get; set; }

    /// <summary>The client secret. Only the directory sync uses it; the browser sign-in never does. Empty leaves the sync off.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>How often the app runs the directory sync, in minutes. Default 15, at least 5. The library does not schedule it.</summary>
    public int SyncMinutes { get; set; } = 15;

    /// <summary>
    /// Whether the directory sync creates an account for each person assigned to the app in Microsoft who has none yet, so they appear before
    /// their first sign-in. Default false: the sync only updates and switches off, and a first sign-in creates. A created account is made as a
    /// first sign-in makes it, with the same roles, the profile hook, the Microsoft login and the same refusals. Read from
    /// <c>SyncCreatesAccounts</c>, true or false.
    /// </summary>
    public bool SyncCreatesAccounts { get; set; }

    /// <summary>
    /// The company domains, lower case, such as <c>example.com</c>. When any is set, the sign-in start refuses an address at another domain
    /// (see <see cref="IsCompanyAddress"/>). Empty, the default, allows every address.
    /// </summary>
    public List<string> AllowedDomains { get; set; } = [];

    /// <summary>
    /// This console's prefix on the role values, such as <c>Api</c> for the value <c>Api.Admin</c>. Null, the default, means the values have no
    /// prefix and each value is a role name. When set, it must be one of <see cref="ConsolePrefixes"/>.
    /// </summary>
    public string? ConsolePrefix { get; set; }

    /// <summary>
    /// The prefixes that name a console on a role value. A value with another console's prefix is left to that console and is not a problem.
    /// Default <c>Api</c> and <c>Map</c>.
    /// </summary>
    public List<string> ConsolePrefixes { get; set; } = ["Api", "Map"];

    /// <summary>
    /// A role value that names the app's rider role. It is reported as a problem and is never a staff role. Null, the default, names none.
    /// </summary>
    public string? RiderRole { get; set; }

    /// <summary>Whether a person has many console roles or exactly one. Default <see cref="MicrosoftRoleMode.Many"/>.</summary>
    public MicrosoftRoleMode RoleMode { get; set; } = MicrosoftRoleMode.Many;

    /// <summary>
    /// A role that gets a session without a permission claim on it, such as the owner's role. Null, the default, means every role needs a
    /// permission claim.
    /// </summary>
    public string? FullAccessRole { get; set; }

    /// <summary>The claim type that marks a role as having a permission. Default <c>permission</c>.</summary>
    public string PermissionClaimType { get; set; } = "permission";

    /// <summary>The cookie scheme that carries the sign-in's result to the complete step, for five minutes. Default <c>MicrosoftSignIn.Cookie</c>.</summary>
    public string CookieScheme { get; set; } = "MicrosoftSignIn.Cookie";

    /// <summary>The OpenID Connect scheme name the app challenges with. Default <c>Microsoft</c>.</summary>
    public string Scheme { get; set; } = "Microsoft";

    /// <summary>The callback path Entra returns to. Must start with a slash. Default <c>/signin-oidc</c>.</summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>The sign-in page that a refusal or a one-time code is redirected to. Must start with a slash. Default <c>/admin/login.html</c>.</summary>
    public string LoginPagePath { get; set; } = "/admin/login.html";

    /// <summary>True when the tenant and the client are set, so the browser sign-in is registered.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>True when sign-in is enabled and a client secret is set, so the directory sync can run.</summary>
    public bool SyncEnabled => IsEnabled && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>The authority that the ID token is checked against, built from the tenant.</summary>
    public string Authority => $"https://login.microsoftonline.com/{TenantId}/v2.0";

    /// <summary>
    /// Whether an address is at one of <see cref="AllowedDomains"/>. When none is set, every address passes. The check ignores case and
    /// surrounding spaces.
    /// </summary>
    /// <param name="address">The address the person typed or Microsoft returned.</param>
    /// <returns>True when the address may start a sign-in.</returns>
    public bool IsCompanyAddress(string? address)
    {
        if (AllowedDomains.Count == 0)
        {
            return true;
        }

        var normalized = (address ?? string.Empty).Trim().ToLowerInvariant();
        var at = normalized.LastIndexOf('@');
        return at > 0 && at < normalized.Length - 1 && AllowedDomains.Contains(normalized[(at + 1)..]);
    }

    /// <summary>
    /// Reads the settings from a configuration section, applies the optional <paramref name="configure"/> action, and checks the result.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="section">The section name. Default <c>MicrosoftSignIn</c>.</param>
    /// <param name="configure">Sets the rest of the options after they are read. Null for none.</param>
    /// <returns>The checked options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="section"/> is null, empty or blank.</exception>
    /// <exception cref="InvalidOperationException">A value cannot be used. See <see cref="Validate"/>.</exception>
    public static MicrosoftSignInOptions Read(IConfiguration configuration, string section = "MicrosoftSignIn", Action<MicrosoftSignInOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var values = configuration.GetSection(section);
        var options = new MicrosoftSignInOptions
        {
            Section = section,
            TenantId = Trimmed(values["TenantId"]),
            ClientId = Trimmed(values["ClientId"]),
            ClientSecret = Trimmed(values["ClientSecret"]),
            SyncMinutes = MinutesOf(Trimmed(values["SyncMinutes"])),
            SyncCreatesAccounts = bool.TryParse(Trimmed(values["SyncCreatesAccounts"]), out var creates) && creates,
            AllowedDomains = SemicolonList(values["Domains"]),
        };

        configure?.Invoke(options);
        options.Validate();
        return options;
    }

    /// <summary>Checks the values. A value that cannot be used is refused at start, not at the first sign-in.</summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant or the client is not a GUID, the sync interval is under 5 minutes, the console prefix is not one of the console prefixes or
    /// contains a dot, or a path does not start with a slash.
    /// </exception>
    public void Validate()
    {
        if (!IsGuidOrEmpty(TenantId))
        {
            throw new InvalidOperationException($"{Section}:TenantId is set but is not a GUID. Copy it from the app registration in Entra, or leave it empty to turn Microsoft sign-in off.");
        }

        if (!IsGuidOrEmpty(ClientId))
        {
            throw new InvalidOperationException($"{Section}:ClientId is set but is not a GUID. Copy it from the app registration in Entra, or leave it empty to turn Microsoft sign-in off.");
        }

        if (SyncMinutes < 5)
        {
            throw new InvalidOperationException($"{Section}:SyncMinutes must be a whole number of minutes, 5 or more.");
        }

        if (ConsolePrefix is not null && (ConsolePrefix.Length == 0 || ConsolePrefix.Contains('.') || !ConsolePrefixes.Contains(ConsolePrefix, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ConsolePrefix must be one of ConsolePrefixes, and a prefix has no dot.");
        }

        if (!CallbackPath.StartsWith('/') || !LoginPagePath.StartsWith('/'))
        {
            throw new InvalidOperationException("CallbackPath and LoginPagePath must start with a slash.");
        }
    }

    private static string? Trimmed(string? value) => value?.Trim();

    private static bool IsGuidOrEmpty(string? value) => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _);

    private static int MinutesOf(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 15;
        }

        // A value that is not a whole number becomes 0, which Validate refuses with the message about the minimum.
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ? minutes : 0;
    }

    private static List<string> SemicolonList(string? text) =>
        (text ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(domain => domain.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
