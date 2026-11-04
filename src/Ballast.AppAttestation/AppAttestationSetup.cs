// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>
/// The values <see cref="AppAttestationRegistration.AddAppAttestation"/> needs once, when the services are set up. They name
/// things an app owns (its package, its token issuer and audience, its header and cookie, its web redirect prefix, its error
/// codes and its texts), so they are set in code and have no library default. Nothing here is read from configuration, so a
/// setting cannot change a name a client depends on.
/// <para>
/// The settings that are bound from configuration are <see cref="AppSessionOptions"/>, <see cref="AppAttestationOptions"/> and
/// <see cref="PlayIntegrityOptions"/>: the signing key, the lifetime, the enforcement switch, and the service account.
/// </para>
/// </summary>
public sealed class AppAttestationSetup
{
    /// <summary>The configuration section <see cref="AppSessionOptions"/> is bound from: <c>AppSession</c>.</summary>
    public const string DefaultSessionSection = "AppSession";

    /// <summary>The configuration section <see cref="AppAttestationOptions"/> is bound from: <c>AppAttestation</c>.</summary>
    public const string DefaultAttestationSection = "AppAttestation";

    /// <summary>The configuration section <see cref="PlayIntegrityOptions"/> is bound from: <c>PlayIntegrity</c>.</summary>
    public const string DefaultPlayIntegritySection = "PlayIntegrity";

    /// <summary>The title of the 401 answer for a data call with no session: <c>App verification required</c>.</summary>
    public const string DefaultSessionRequiredTitle = "App verification required";

    /// <summary>The configuration section the session settings come from. It must not be blank. Default <c>AppSession</c>.</summary>
    public string SessionSection { get; set; } = DefaultSessionSection;

    /// <summary>The configuration section the attestation settings come from. It must not be blank. Default <c>AppAttestation</c>.</summary>
    public string AttestationSection { get; set; } = DefaultAttestationSection;

    /// <summary>The configuration section the Google settings come from. It must not be blank. Default <c>PlayIntegrity</c>.</summary>
    public string PlayIntegritySection { get; set; } = DefaultPlayIntegritySection;

    /// <summary>
    /// The Android application id a verdict must name, for example <c>com.example.app</c>. Required: the library has no default, so a
    /// verdict for another app can never be accepted by accident.
    /// </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>The <c>iss</c> of a session token. Required. A user token from the same host never has this issuer and audience together.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The <c>aud</c> of a session token. Required, and different from any user token's audience, so a user token is never read as a session.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>The request header a client sends its session in, for example <c>X-App-Session</c>. Required.</summary>
    public string HeaderName { get; set; } = string.Empty;

    /// <summary>The cookie a web view carries its session in, set by the ticket redemption, for example <c>app_session</c>. Required.</summary>
    public string CookieName { get; set; } = string.Empty;

    /// <summary>
    /// The path prefix a web ticket may redirect to, for example <c>/map/</c>. It must start and end with a slash. Required. A
    /// <c>next</c> that does not begin with it, or that is absolute, is refused.
    /// </summary>
    public string RedirectPrefix { get; set; } = string.Empty;

    /// <summary>
    /// The platform names a client may report, lower case. Default: <c>android</c>, <c>ios</c>, <c>maccatalyst</c>, <c>windows</c>.
    /// Any other platform is a 400.
    /// </summary>
    public string[] Platforms { get; set; } = ["android", "ios", "maccatalyst", "windows"];

    /// <summary>The title of the 401 answer. Default <see cref="DefaultSessionRequiredTitle"/>.</summary>
    public string SessionRequiredTitle { get; set; } = DefaultSessionRequiredTitle;

    /// <summary>The detail of the 401 answer, which says in the app's own words what to do. Required.</summary>
    public string SessionRequiredDetail { get; set; } = string.Empty;

    /// <summary>The code of the 401 answer. Required. <see cref="AppAttestationCodes.SessionRequired"/> is the stable value.</summary>
    public string SessionRequiredCode { get; set; } = string.Empty;

    /// <summary>The code of the 403 answer. Required. <see cref="AppAttestationCodes.AttestationFailed"/> is the stable value.</summary>
    public string AttestationFailedCode { get; set; } = string.Empty;

    /// <summary>The code of the 503 answer. Required. <see cref="AppAttestationCodes.AttestationUnavailable"/> is the stable value.</summary>
    public string AttestationUnavailableCode { get; set; } = string.Empty;

    /// <summary>
    /// Checks the values, and throws when one is missing or not usable. It is called by <see cref="AppAttestationRegistration.AddAppAttestation"/>
    /// before anything is registered, so a missing value stops the start, never a first request.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is blank, or the redirect prefix, the header, the cookie or a platform name is not usable. The message names the property.</exception>
    public void Validate()
    {
        Require(SessionSection, nameof(SessionSection));
        Require(AttestationSection, nameof(AttestationSection));
        Require(PlayIntegritySection, nameof(PlayIntegritySection));
        Require(PackageName, nameof(PackageName));
        Require(Issuer, nameof(Issuer));
        Require(Audience, nameof(Audience));
        Require(SessionRequiredTitle, nameof(SessionRequiredTitle));
        Require(SessionRequiredDetail, nameof(SessionRequiredDetail));
        Require(SessionRequiredCode, nameof(SessionRequiredCode));
        Require(AttestationFailedCode, nameof(AttestationFailedCode));
        Require(AttestationUnavailableCode, nameof(AttestationUnavailableCode));

        if (!IsHeaderName(HeaderName))
            throw Invalid(nameof(HeaderName), "must be a header name with no spaces, colons or non-ASCII characters.");

        if (!IsCookieName(CookieName))
            throw Invalid(nameof(CookieName), "must be a cookie name with no spaces, semicolons, commas, equals signs or non-ASCII characters.");

        if (!IsRedirectPrefix(RedirectPrefix))
            throw Invalid(nameof(RedirectPrefix), "must start and end with a slash, be printable ASCII, and hold no backslash, for example /map/.");

        if (Platforms.Length == 0)
            throw Invalid(nameof(Platforms), "must name at least one platform.");

        foreach (var platform in Platforms)
        {
            if (string.IsNullOrWhiteSpace(platform) || !string.Equals(platform, platform.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                throw Invalid(nameof(Platforms), $"must hold lower-case platform names with no spaces; '{platform}' does not.");
        }
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid(name, "must be set. The app sets it in the AddAppAttestation call.");
    }

    private static InvalidOperationException Invalid(string name, string rule) =>
        new($"{nameof(AppAttestationSetup)}.{name} {rule}");

    private static bool IsHeaderName(string value) =>
        value.Length > 0 && value.All(c => c > 0x20 && c < 0x7F && c != ':');

    private static bool IsCookieName(string value) =>
        value.Length > 0 && value.All(c => c > 0x20 && c < 0x7F && c is not (';' or ',' or '='));

    private static bool IsRedirectPrefix(string value) =>
        value.Length >= 2 && value[0] == '/' && value[^1] == '/' && value.All(c => c > 0x20 && c < 0x7F && c != '\\');
}
