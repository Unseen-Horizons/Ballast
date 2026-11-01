// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>Options for a client made with <see cref="SafeHttpClientExtensions.AddSafeHttpClient"/>.</summary>
public sealed class SafeHttpClientOptions
{
    /// <summary>
    /// The User-Agent header every request sends, as a product token with an optional comment, for example
    /// <c>ExampleApp/1.0 (+https://example.test/bot)</c>. Required: an empty value or one that is not a valid product is refused
    /// at registration. There is no default, so the library never sends a name that is not the app's own.
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// The most time one request may take, from the start of the send to the last byte of the response headers the client reads.
    /// Default 60 seconds. It must be above zero.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Development only: when true, the connect-time check is not installed, so the client may reach loopback and private
    /// addresses. Production keeps it false. Default false.
    /// </summary>
    public bool AllowNonPublicAddresses { get; set; }
}
