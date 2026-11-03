// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The password rules, applied to Identity's password validator. Default: 12 characters with a digit, an
/// upper-case letter and a symbol. An app sets its own here, for example 8 characters with an upper-case letter, a lower-case letter and a digit.
/// </summary>
public sealed class AuthPasswordRules
{
    /// <summary>The shortest password. Default 12. Must be at least 1.</summary>
    public int RequiredLength { get; set; } = 12;

    /// <summary>Whether a password needs a digit. Default true.</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>Whether a password needs an upper-case letter. Default true.</summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>Whether a password needs a lower-case letter. Default false.</summary>
    public bool RequireLowercase { get; set; }

    /// <summary>Whether a password needs a symbol (anything that is not a letter or a digit). Default true.</summary>
    public bool RequireNonAlphanumeric { get; set; } = true;

    /// <summary>How many different characters a password must contain. Default 1, which imposes nothing. Must be at least 1.</summary>
    public int RequiredUniqueChars { get; set; } = 1;
}
