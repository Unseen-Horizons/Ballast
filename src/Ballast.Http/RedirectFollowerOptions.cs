// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>Options for a <see cref="RedirectFollower"/>. The defaults are the strictest rules.</summary>
public sealed class RedirectFollowerOptions
{
    /// <summary>
    /// The most redirects one request may follow. A redirect that would be the next one after this many is refused with
    /// <see cref="HttpSafetyReason.TooManyRedirects"/>, so a chain of six redirects sends six requests and no more. Default 5.
    /// </summary>
    public int MaxRedirects { get; set; } = 5;

    /// <summary>
    /// Whether every address in the chain, the first one included, must be https. Default true. When false, plain http is
    /// allowed, but an https address may still never redirect to http (<see cref="HttpSafetyReason.SchemeDowngrade"/>).
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Development only: skips the public-address check before each request. It must match the registration: a client made with
    /// <see cref="SafeHttpClientOptions.AllowNonPublicAddresses"/> = true. Production keeps it false. Default false.
    /// </summary>
    public bool AllowNonPublicAddresses { get; set; }
}
