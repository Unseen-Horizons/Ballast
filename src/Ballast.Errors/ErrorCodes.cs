// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Errors;

/// <summary>
/// The error codes that any API can use. A code is a public contract: once released, a code is never renamed, reused or
/// changed, and a new condition gets a new code. A client translates the code and never reads the message.
/// </summary>
public static class ErrorCodes
{
    /// <summary>The item asked for does not exist.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>A value in the request is missing or invalid.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>A service this API depends on could not be reached, or did not give a usable answer.</summary>
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";

    /// <summary>An unexpected failure on the server. Its answer carries only a trace reference.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>The caller has sent too many requests in a period and must wait before sending more.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>The server is busy with a shared resource, such as the database, and the request can be tried again.</summary>
    public const string ServiceBusy = "SERVICE_BUSY";

    /// <summary>The URL names no endpoint.</summary>
    public const string EndpointNotFound = "ENDPOINT_NOT_FOUND";
}
