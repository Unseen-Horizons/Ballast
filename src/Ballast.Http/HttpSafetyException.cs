// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>
/// An outbound request that the safety rules refused. It carries the <see cref="HttpSafetyReason"/> for the app to map to its
/// own error code, and the URL with its user information, query and fragment removed (see <see cref="UrlForLog"/>). The message
/// names hosts and what was refused, never a credential.
/// </summary>
public sealed class HttpSafetyException : Exception
{
    /// <summary>Creates the refusal.</summary>
    /// <param name="reason">Why the request was refused.</param>
    /// <param name="message">The sentence for a log or a developer. It must not contain a credential.</param>
    /// <param name="url">The URL the refusal is about, or null when none. It is stored redacted.</param>
    /// <param name="inner">The exception this one came from, or null.</param>
    public HttpSafetyException(HttpSafetyReason reason, string message, Uri? url = null, Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
        RedactedUrl = url is null ? null : UrlForLog.Redact(url);
    }

    /// <summary>Why the request was refused.</summary>
    public HttpSafetyReason Reason { get; }

    /// <summary>The URL the refusal is about, with its user information, query and fragment removed. Null when there was none.</summary>
    public string? RedactedUrl { get; }
}
