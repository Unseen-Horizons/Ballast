// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The lockout rules, applied to Identity's lockout options. An account locked by failed sign-ins is refused at a password
/// sign-in, at a refresh and on every request until its lockout ends. Default: five failures, then a lockout of 15 minutes.
/// </summary>
public sealed class AuthLockoutRules
{
    /// <summary>How many failed sign-ins lock the account. Default 5. Must be at least 1.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>How long the lockout lasts. Default 15 minutes. Must be positive.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Whether an account that has never signed in can be locked too. Default true.</summary>
    public bool AllowedForNewUsers { get; set; } = true;
}
