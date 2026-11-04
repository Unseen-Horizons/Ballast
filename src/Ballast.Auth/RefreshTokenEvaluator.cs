// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>What a presented refresh token entitles the caller to.</summary>
public enum RefreshTokenVerdict
{
    /// <summary>Valid: revoke it and issue a replacement.</summary>
    Rotate,

    /// <summary>Already rotated once and presented again. Treated as theft: every live token of the user is revoked.</summary>
    ReuseDetected,

    /// <summary>Unknown, expired or revoked.</summary>
    Invalid,
}

/// <summary>
/// Decides what to do with a presented refresh token. The rules are short, and each one is load-bearing, so the decision is kept
/// apart from the database writes that carry it out.
/// </summary>
/// <remarks>
/// Rotation plus reuse detection is the whole of the theft response. A stolen token replayed after the legitimate client has refreshed
/// hits <see cref="RefreshTokenVerdict.ReuseDetected"/>, and the user's whole family is revoked. The caller's address is not an input:
/// the library does not store one, and the phone changes address too often for it to mean anything.
/// </remarks>
public static class RefreshTokenEvaluator
{
    /// <summary>Decides a presented token's verdict.</summary>
    /// <param name="found">Whether a stored token matched the presented hash.</param>
    /// <param name="hasReplacement">Whether the stored token already names a successor.</param>
    /// <param name="isActive">Whether the stored token is neither expired nor revoked.</param>
    /// <returns>The verdict.</returns>
    public static RefreshTokenVerdict Evaluate(bool found, bool hasReplacement, bool isActive)
    {
        if (!found)
        {
            return RefreshTokenVerdict.Invalid;
        }

        // Tested before the active check. Rotation sets RevokedAt and ReplacedByToken together, so a replayed token is already
        // inactive; checking the active state first would send every stolen token down the ordinary expired path, and the theft
        // would never be seen.
        if (hasReplacement)
        {
            return RefreshTokenVerdict.ReuseDetected;
        }

        if (!isActive)
        {
            return RefreshTokenVerdict.Invalid;
        }

        return RefreshTokenVerdict.Rotate;
    }
}
