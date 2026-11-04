// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The settings of Google sign-in: the Web client id the app's ID tokens are issued for, and the role a new Google account gets. Set by
/// <see cref="AuthGoogleRegistration.AddBallastGoogleSignIn{TUser,TKey,TRefresh,TContext}"/>, which reads <c>WebClientId</c> from its
/// configuration section.
/// </summary>
public sealed class AuthGoogleOptions
{
    /// <summary>
    /// The Web client id (the OAuth client the app's Google sign-in is configured with). A token must be issued for this audience. Null
    /// or blank means Google sign-in is not configured: every Google call answers <see cref="AuthFailures.GoogleSignInNotConfigured"/>.
    /// The server still starts without it.
    /// </summary>
    public string? WebClientId { get; set; }

    /// <summary>
    /// The role a new Google account is put in, such as <c>User</c>. The role must already exist. Null (the default) gives the account no role.
    /// </summary>
    public string? NewAccountRole { get; set; }
}
