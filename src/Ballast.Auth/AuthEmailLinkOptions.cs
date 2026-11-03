// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The settings of the single-use e-mail sign-in link: how long a link lives, how many a subject may ask for in a window, and the
/// window itself. Set them in the <c>configure</c> action of <see cref="AuthEmailLinkRegistration.AddBallastEmailLinks{TContext}"/>.
/// </summary>
public sealed class AuthEmailLinkOptions
{
    /// <summary>The longest lifetime a link may be given: one day.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a link works after it is issued. Default 15 minutes. It must be greater than zero and at most 24 hours. A link that
    /// expires is refused the same way as a used one.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many links one subject may be sent for one purpose in <see cref="RateWindow"/>. Default 5. It must be at least 1. The
    /// request beyond it is refused with <see cref="AuthFailures.EmailLinkRateLimited"/>, and no e-mail is sent.
    /// </summary>
    public int MaxIssuedPerWindow { get; set; } = 5;

    /// <summary>
    /// The window the limit of <see cref="MaxIssuedPerWindow"/> counts over. Default one hour. It must be greater than zero. A link
    /// issued at the start of the window stops counting when the window has passed it.
    /// </summary>
    public TimeSpan RateWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Checks the values, and throws when one of them cannot be used.</summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Lifetime"/> is not greater than zero and at most 24 hours, <see cref="MaxIssuedPerWindow"/> is below 1, or
    /// <see cref="RateWindow"/> is not greater than zero.
    /// </exception>
    public void Validate()
    {
        if (Lifetime <= TimeSpan.Zero || Lifetime > MaxLifetime)
        {
            throw new InvalidOperationException("AuthEmailLinkOptions.Lifetime must be greater than zero and at most 24 hours.");
        }

        if (MaxIssuedPerWindow < 1)
        {
            throw new InvalidOperationException("AuthEmailLinkOptions.MaxIssuedPerWindow must be at least 1.");
        }

        if (RateWindow <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("AuthEmailLinkOptions.RateWindow must be greater than zero.");
        }
    }
}
