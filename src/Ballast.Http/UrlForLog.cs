// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>
/// The one rule for a URL that goes into a log, a warning, an audit row or an exception: scheme, host, port and path, with the
/// query replaced by <c>?[redacted]</c> so a reader sees that there was one. User information and the fragment are dropped.
/// Text that is not an absolute URL is never echoed: it becomes <c>(not an absolute URL)</c>.
/// </summary>
public static class UrlForLog
{
    /// <summary>What a non-absolute address is written as, so that none of its text reaches a log.</summary>
    public const string NotAnAbsoluteUrl = "(not an absolute URL)";

    /// <summary>What a query string is written as.</summary>
    public const string RedactedQuery = "?[redacted]";

    /// <summary>
    /// The redacted form of <paramref name="url"/>. A port is shown only when it is not the scheme's default. A list of URLs
    /// separated by <c>;</c> is one URL to this method: an app that stores a list redacts each address on its own.
    /// </summary>
    /// <param name="url">The URL as it was written. Required.</param>
    /// <returns>The redacted URL, or <see cref="NotAnAbsoluteUrl"/> when the text is not an absolute URL with a host.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="url"/> is null.</exception>
    public static string Redact(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Redact(uri) : NotAnAbsoluteUrl;
    }

    /// <summary>The redacted form of <paramref name="uri"/>. See <see cref="Redact(string)"/>.</summary>
    /// <param name="uri">The URL. Required.</param>
    /// <returns>The redacted URL, or <see cref="NotAnAbsoluteUrl"/> when the URL has no host.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is null.</exception>
    public static string Redact(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || string.IsNullOrEmpty(uri.Host))
        {
            return NotAnAbsoluteUrl;
        }

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var query = uri.Query.Length > 0 ? RedactedQuery : string.Empty;
        return $"{uri.Scheme}://{uri.Host}{port}{uri.AbsolutePath}{query}";
    }
}
