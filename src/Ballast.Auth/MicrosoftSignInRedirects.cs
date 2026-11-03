// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Auth;

/// <summary>
/// The redirect addresses of Microsoft staff sign-in, and the reading of Microsoft's own error codes. The browser gets the refusal and, when
/// there is one, Microsoft's code; it never gets Microsoft's message, which can carry text the person should not see.
/// </summary>
public static class MicrosoftSignInRedirects
{
    private static readonly Regex AadstsCode = new(@"AADSTS[0-9]{4,7}", RegexOptions.CultureInvariant);
    private static readonly Regex OAuthErrorWord = new(@"^[a-z_]{1,40}\z", RegexOptions.CultureInvariant);

    /// <summary>The address of a refusal on the sign-in page.</summary>
    /// <param name="loginPagePath">The sign-in page, from <see cref="MicrosoftSignInOptions.LoginPagePath"/>.</param>
    /// <param name="refusal">The refusal, such as <see cref="MicrosoftRefusal.SignInFailed"/>.</param>
    /// <param name="providerCode">Microsoft's own code, or null for none.</param>
    /// <returns>The path and query.</returns>
    public static string Error(string loginPagePath, string refusal, string? providerCode = null) =>
        providerCode is null
            ? $"{loginPagePath}?error={Uri.EscapeDataString(refusal)}"
            : $"{loginPagePath}?error={Uri.EscapeDataString(refusal)}&ms={Uri.EscapeDataString(providerCode)}";

    /// <summary>The address that carries a one-time code to the sign-in page, which exchanges it for a session.</summary>
    /// <param name="loginPagePath">The sign-in page, from <see cref="MicrosoftSignInOptions.LoginPagePath"/>.</param>
    /// <param name="code">The one-time code.</param>
    /// <returns>The path and query.</returns>
    public static string Code(string loginPagePath, string code) => $"{loginPagePath}?code={Uri.EscapeDataString(code)}";

    /// <summary>
    /// Microsoft's own code for a failure: the AADSTS number in the failure message, or else the OAuth error word of the callback. Null when
    /// neither is present.
    /// </summary>
    /// <param name="failureMessage">The failure's message, which is read and never shown.</param>
    /// <param name="oauthError">The <c>error</c> field of the callback form, or null.</param>
    /// <returns>The code, or null.</returns>
    public static string? ProviderCode(string? failureMessage, string? oauthError)
    {
        var aadsts = failureMessage is null ? string.Empty : AadstsCode.Match(failureMessage).Value;
        if (aadsts.Length > 0)
        {
            return aadsts;
        }

        return oauthError is not null && OAuthErrorWord.IsMatch(oauthError) ? oauthError : null;
    }
}
