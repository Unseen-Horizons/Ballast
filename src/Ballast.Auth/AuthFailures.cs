// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The refusals of the sign-in core, with their codes, statuses and messages. The defaults are the codes below,
/// which clients translate. An app that shipped other codes sets them here, for example a 400 for invalid
/// credentials. A new condition gets a new code; a released code is never renamed.
/// </summary>
public sealed class AuthFailures
{
    /// <summary>
    /// The answer to every sign-in that cannot be proven: an unknown address, a wrong password, a locked or password-less account,
    /// a Microsoft account, and a user outside the password sign-in mode. It is the same answer for all of them.
    /// </summary>
    public AuthFailure InvalidCredentials { get; set; } = new("INVALID_CREDENTIALS", 401, "Invalid credentials.");

    /// <summary>The answer to a password sign-in when <see cref="AuthOptions.PasswordSignIn"/> is <see cref="AuthPasswordSignInMode.Off"/>.</summary>
    public AuthFailure PasswordSignInOff { get; set; } = new("PASSWORD_SIGNIN_OFF", 403, "Password sign-in is turned off.");

    /// <summary>A refresh token that is unknown, expired, revoked, reused, or whose account is locked. One answer for all of them.</summary>
    public AuthFailure RefreshTokenExpired { get; set; } = new("REFRESH_TOKEN_EXPIRED", 401, "Refresh token is invalid or expired.");

    /// <summary>A refresh token that is blank.</summary>
    public AuthFailure InvalidRefreshToken { get; set; } = new("INVALID_REFRESH_TOKEN", 401, "Invalid refresh token.");

    /// <summary>The account named by a signed-in request or an admin action does not exist.</summary>
    public AuthFailure UserNotFound { get; set; } = new("USER_NOT_FOUND", 404, "User not found");

    /// <summary>An action that a Microsoft account cannot take, such as a password change or a Google link.</summary>
    public AuthFailure MicrosoftAccount { get; set; } = new("MICROSOFT_ACCOUNT", 409, "This account signs in with Microsoft.");

    /// <summary>A password change on an account that has no password.</summary>
    public AuthFailure NoPasswordSet { get; set; } = new("NO_PASSWORD_SET", 409, "This account has no password. Set one instead.");

    /// <summary>A first password set on an account that already has one.</summary>
    public AuthFailure PasswordAlreadySet { get; set; } = new("PASSWORD_ALREADY_SET", 409, "This account already has a password.");

    /// <summary>
    /// An admin's password reset on an account that has no password, such as one made by Google or Microsoft sign-in. The reset does not set a
    /// first password on such an account; the person signs in another way.
    /// </summary>
    public AuthFailure NoPasswordAccount { get; set; } = new("NO_PASSWORD_ACCOUNT", 422, "This account has no password, so it cannot be reset.");

    /// <summary>
    /// A sign-in link that is unknown, already used, expired, or for another purpose. One answer for all of them, so a link cannot be
    /// probed.
    /// </summary>
    public AuthFailure EmailLinkInvalid { get; set; } = new("EMAIL_LINK_INVALID", 401, "This link has been used or has expired.");

    /// <summary>More sign-in links were asked for in the window than the options allow.</summary>
    public AuthFailure EmailLinkRateLimited { get; set; } = new("RATE_LIMITED", 429, "Too many links were requested. Try again later.");

    /// <summary>A Google ID token that Google does not vouch for: a bad signature, another app's token, an expired one, or a token that is not one.</summary>
    public AuthFailure GoogleTokenInvalid { get; set; } = new("GOOGLE_TOKEN_INVALID", 401, "Google sign-in failed.");

    /// <summary>Google sign-in is asked for but no Web client id is configured for the app.</summary>
    public AuthFailure GoogleSignInNotConfigured { get; set; } = new("GOOGLE_SIGNIN_NOT_CONFIGURED", 503, "Google sign-in is not configured.");

    /// <summary>Google could not be reached to check the token. That is a network problem, not a bad token, so the rider may retry.</summary>
    public AuthFailure GoogleUnreachable { get; set; } = new("GOOGLE_UNREACHABLE", 503, "Google could not be reached.");

    /// <summary>The Google address is not verified by Google, so it cannot create or link an account.</summary>
    public AuthFailure GoogleEmailUnverified { get; set; } = new("GOOGLE_EMAIL_UNVERIFIED", 403, "Your Google email address is not verified.");

    /// <summary>
    /// A first Google sign-in whose address belongs to a password account. The Google account is never linked to it automatically, because
    /// the password account's address was never proven to belong to the person.
    /// </summary>
    public AuthFailure AccountExistsUsePassword { get; set; } = new("ACCOUNT_EXISTS_USE_PASSWORD", 409, "An account with this email already exists.");

    /// <summary>A link when the account already has a Google login, or when the Google account is linked to another user.</summary>
    public AuthFailure GoogleAlreadyLinked { get; set; } = new("GOOGLE_ALREADY_LINKED", 409, "A Google account is already linked.");

    /// <summary>An unlink that would leave the account with no way to sign in: it has no password.</summary>
    public AuthFailure LastSignInMethod { get; set; } = new("LAST_SIGN_IN_METHOD", 409, "Set a password before unlinking Google.");

    /// <summary>An unlink when no Google login is on the account.</summary>
    public AuthFailure GoogleNotLinked { get; set; } = new("GOOGLE_NOT_LINKED", 404, "No Google account is linked.");

    /// <summary>
    /// A Microsoft one-time code that is unknown, used, expired, or whose account is no longer a signed-in Microsoft account. One answer for all
    /// of them, so a code cannot be probed.
    /// </summary>
    public AuthFailure MicrosoftCodeInvalid { get; set; } = new("SIGNIN_CODE_INVALID", 400, "The sign-in expired. Try again.");

    /// <summary>
    /// The code for a refusal from Identity that has no code of its own. The message is the Identity text, so a client shows
    /// the server's own words. Only the status and the code are set here; the message is replaced by Identity's.
    /// </summary>
    public AuthFailure Unclassified { get; set; } = new("UNCLASSIFIED", 400, "The request was refused.");
}
