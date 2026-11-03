// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

namespace Ballast.Auth;

/// <summary>
/// The check every request with an access token passes: the subject still names an account that may sign in, and the token's security
/// stamp is the account's current one. A token issued before the stamp existed carries no stamp; it is accepted unless the options require one.
/// </summary>
internal static class AuthAccessCheck
{
    /// <summary>Whether a validated token's account still stands.</summary>
    /// <param name="principal">The validated principal.</param>
    /// <param name="options">The options, for the claim names and the stamp rule.</param>
    /// <param name="store">The account store.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>True when the request may proceed.</returns>
    public static async Task<bool> AcceptsAsync(ClaimsPrincipal? principal, AuthOptions options, IAuthAccountStore store, CancellationToken ct)
    {
        var subject = principal?.FindFirst(options.Claims.Subject)?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            return false;
        }

        var state = await store.FindAsync(subject, ct);
        if (state is null || !state.CanSignIn)
        {
            return false;
        }

        var stamp = principal!.FindFirst(options.Claims.SecurityStamp)?.Value;
        if (stamp is null)
        {
            return !options.SecurityStampRequired;
        }

        return string.Equals(stamp, state.SecurityStamp, StringComparison.Ordinal);
    }
}
