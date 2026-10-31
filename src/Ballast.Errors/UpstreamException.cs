// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Errors;

/// <summary>
/// A failed call to an upstream service. It is an <see cref="AppException"/> with status 502. It also keeps the upstream's
/// host, the status the upstream answered with and the address that was called, for logs and diagnostics.
/// <see cref="Problem.From(AppException, string?, string?)"/> writes none of these three, so anything the client may see
/// belongs in the detail or in the extensions.
/// </summary>
public class UpstreamException : AppException
{
    /// <summary>
    /// Creates a failed upstream call with status 502.
    /// </summary>
    /// <param name="host">The upstream's host name. Required, and a blank host is refused.</param>
    /// <param name="upstreamStatusCode">The status the upstream answered with. It is stored as given and not checked.</param>
    /// <param name="requestUri">The address that was called. Required, and a blank address is refused. It may be written to
    /// logs, so remove any secret from it, such as a key in the query, before it is passed in.</param>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The message, which becomes the problem title.</param>
    /// <param name="detail">The developer sentence, or null to use the message.</param>
    /// <param name="extensions">Extra members for the answer, or null for none.</param>
    /// <exception cref="ArgumentException">
    /// The host or the address is blank, or the code, the message, the detail or an extension name is refused as in
    /// <see cref="AppException"/>.
    /// </exception>
    public UpstreamException(string host, int upstreamStatusCode, string requestUri, string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null)
        : base(code, message, 502, detail, extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        Host = host;
        UpstreamStatusCode = upstreamStatusCode;
        RequestUri = requestUri;
    }

    /// <summary>The upstream's host name.</summary>
    public string Host { get; }

    /// <summary>The status the upstream answered with, as the caller gave it.</summary>
    public int UpstreamStatusCode { get; }

    /// <summary>The address that was called.</summary>
    public string RequestUri { get; }
}
