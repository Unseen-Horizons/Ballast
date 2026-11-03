// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;

using Microsoft.Extensions.Configuration;

namespace Ballast.Auth;

/// <summary>
/// Everything the sign-in library reads from an app: the token settings (read from the <c>Jwt</c> section by default), the
/// password and lockout rules, the sign-in mode, the claim names and the refusals. An app binds them once at startup with
/// <see cref="AuthRegistration.AddBallastAuth{TUser,TKey,TRole,TContext,TRefresh}"/>, and <see cref="Validate"/> refuses a value
/// the library cannot honour, so a misconfiguration stops the app at startup rather than at the first sign-in.
/// </summary>
/// <remarks>
/// Every default is stated on its property. An app that sets only its key, issuer and audience gets all the others. The refresh token
/// lifetime, the access token lifetime and the key come from configuration; the rest come from the app's own code.
/// </remarks>
public sealed class AuthOptions
{
    /// <summary>The access token lifetime when <c>ExpiryMinutes</c> is absent or blank: 15 minutes.</summary>
    public const double DefaultAccessTokenMinutes = 15;

    /// <summary>The longest access token lifetime <c>ExpiryMinutes</c> may set: a day.</summary>
    public const double MaxAccessTokenMinutes = 1440;

    /// <summary>The fewest UTF-8 bytes a signing key may have.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The placeholder a key is left with when it was never set. It is refused.</summary>
    public const string PlaceholderKey = "CHANGE-ME";

    /// <summary>
    /// The configuration section the token settings were read from, for the messages of <see cref="Validate"/>. The default
    /// is <c>Jwt</c>, the usual section name. Set by <see cref="ReadJwt"/>.
    /// </summary>
    public string JwtSection { get; set; } = "Jwt";

    /// <summary>The HMAC-SHA256 signing key. Required, at least 32 UTF-8 bytes, never the placeholder. Read from <c>Key</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The token issuer, checked on every request. Required. Read from <c>Issuer</c>.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The token audience, checked on every request. Required. Read from <c>Audience</c>.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The access token lifetime in minutes, greater than 0 and at most 1440. Read from <c>ExpiryMinutes</c>; absent or blank means
    /// 15 minutes, and a value that is not such a number stops the start.
    /// </summary>
    public double AccessTokenMinutes { get; set; } = DefaultAccessTokenMinutes;

    /// <summary>
    /// The refresh token lifetime in days for a sign-in without remember-me. At least 1. Read from <c>RefreshTokenDays</c>
    /// (default 1). It is also how a stored refresh token is recognised as a remember-me one (see <see cref="RefreshTokenDaysRememberMe"/>).
    /// </summary>
    public int RefreshTokenDays { get; set; } = 1;

    /// <summary>
    /// The refresh token lifetime in days for a sign-in with remember-me. Greater than <see cref="RefreshTokenDays"/>, so the two
    /// can be told apart. Read from <c>RefreshTokenDaysRememberMe</c> (default 30).
    /// </summary>
    public int RefreshTokenDaysRememberMe { get; set; } = 30;

    /// <summary>The clock skew allowed on a token's expiry. Default 30 seconds, so a 15-minute token is about 15 minutes.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether a token without a security stamp claim is refused. Default false: a token issued before the stamp existed carries no
    /// claim, is accepted, and runs out within its lifetime (15 minutes by default). Set it to true once those tokens have expired.
    /// </summary>
    public bool SecurityStampRequired { get; set; }

    /// <summary>
    /// Whether a bcrypt password hash, as older code stored it, is accepted. When it is, a successful sign-in rewrites
    /// the hash in Identity's format (Identity's <c>SuccessRehashNeeded</c>). Default false.
    /// </summary>
    public bool LegacyBcryptPasswords { get; set; }

    /// <summary>Who may sign in with a password. Default <see cref="AuthPasswordSignInMode.All"/>.</summary>
    public AuthPasswordSignInMode PasswordSignIn { get; set; } = AuthPasswordSignInMode.All;

    /// <summary>
    /// The one role that <see cref="AuthPasswordSignInMode.OwnerOnly"/> admits. Required in that mode, and ignored otherwise. The
    /// library does not know which role an app means by it.
    /// </summary>
    public string? PrivilegedRoleName { get; set; }

    /// <summary>
    /// The role a new account is given when it is created by a first sign-in (Google, or a Microsoft staff account). Null gives no
    /// role, for example <c>User</c>.
    /// </summary>
    public string? NewAccountRole { get; set; }

    /// <summary>The claim names of an access token.</summary>
    public AuthClaimNames Claims { get; set; } = new();

    /// <summary>The refusals the library gives, with their codes, statuses and messages.</summary>
    public AuthFailures Failures { get; set; } = new();

    /// <summary>The password rules.</summary>
    public AuthPasswordRules Password { get; set; } = new();

    /// <summary>The lockout rules.</summary>
    public AuthLockoutRules Lockout { get; set; } = new();

    /// <summary>
    /// Reads the token settings from a configuration section (the <c>Jwt</c> section by default): <c>Key</c>, <c>Issuer</c>,
    /// <c>Audience</c>, <c>ExpiryMinutes</c>, <c>RefreshTokenDays</c> and <c>RefreshTokenDaysRememberMe</c>. A missing value keeps
    /// its default; a value that is present but cannot be read is kept as an error for <see cref="Validate"/> to report.
    /// </summary>
    /// <param name="section">The section to read. Its path names the section in the messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A lifetime value is present and is not a number in its range.</exception>
    public void ReadJwt(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (!string.IsNullOrWhiteSpace(section.Path))
        {
            JwtSection = section.Path;
        }

        Key = section["Key"] ?? string.Empty;
        Issuer = section["Issuer"] ?? string.Empty;
        Audience = section["Audience"] ?? string.Empty;
        AccessTokenMinutes = ReadMinutes(section, "ExpiryMinutes");
        RefreshTokenDays = ReadWhole(section, "RefreshTokenDays", 1);
        RefreshTokenDaysRememberMe = ReadWhole(section, "RefreshTokenDaysRememberMe", 30);
    }

    /// <summary>
    /// Checks the options. <see cref="AuthRegistration.AddBallastAuth{TUser,TKey,TRole,TContext,TRefresh}"/> calls this, so a bad value
    /// stops the app at startup. An app that changes the options after registration can call it again.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is missing or out of range. The message names the setting.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new InvalidOperationException($"{JwtSection}:Key is not set. Set it in user secrets or an environment variable, at least {MinimumKeyBytes} bytes long.");
        }

        if (string.Equals(Key.Trim(), PlaceholderKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{JwtSection}:Key is still the placeholder '{PlaceholderKey}'. Set a real secret.");
        }

        if (Encoding.UTF8.GetByteCount(Key) < MinimumKeyBytes)
        {
            throw new InvalidOperationException($"{JwtSection}:Key is shorter than {MinimumKeyBytes} bytes. Set a longer secret.");
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException($"{JwtSection}:Issuer is not set.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException($"{JwtSection}:Audience is not set.");
        }

        if (!(AccessTokenMinutes > 0 && AccessTokenMinutes <= MaxAccessTokenMinutes))
        {
            throw new InvalidOperationException($"{JwtSection}:ExpiryMinutes is not a number of minutes greater than 0 and at most {MaxAccessTokenMinutes}.");
        }

        if (RefreshTokenDays < 1)
        {
            throw new InvalidOperationException($"{JwtSection}:RefreshTokenDays must be at least 1.");
        }

        if (RefreshTokenDaysRememberMe <= RefreshTokenDays)
        {
            throw new InvalidOperationException($"{JwtSection}:RefreshTokenDaysRememberMe must be greater than {JwtSection}:RefreshTokenDays, so a remember-me token can be told apart.");
        }

        if (ClockSkew < TimeSpan.Zero)
        {
            throw new InvalidOperationException("The clock skew cannot be negative.");
        }

        if (PasswordSignIn == AuthPasswordSignInMode.OwnerOnly && string.IsNullOrWhiteSpace(PrivilegedRoleName))
        {
            throw new InvalidOperationException("PasswordSignIn is OwnerOnly, so PrivilegedRoleName must name the role that may sign in.");
        }

        if (Password.RequiredLength < 1 || Password.RequiredUniqueChars < 1)
        {
            throw new InvalidOperationException("The password rules need RequiredLength and RequiredUniqueChars of at least 1.");
        }

        if (Lockout.MaxFailedAccessAttempts < 1 || Lockout.LockoutDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The lockout rules need MaxFailedAccessAttempts of at least 1 and a positive LockoutDuration.");
        }
    }

    private static double ReadMinutes(IConfigurationSection section, string key)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultAccessTokenMinutes;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0 && minutes <= MaxAccessTokenMinutes)
        {
            return minutes;
        }

        throw new InvalidOperationException($"{section.Path}:{key} is not a number of minutes greater than 0 and at most {MaxAccessTokenMinutes}.");
    }

    private static int ReadWhole(IConfigurationSection section, string key, int fallback)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"{section.Path}:{key} is not a whole number.");
    }
}
