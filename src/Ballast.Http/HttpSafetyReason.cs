// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>
/// Why an outbound request was refused before, or while, it was sent. Each value is stable: an app maps it to its own error
/// code, and a value is never renamed, reused or removed once released.
/// </summary>
public enum HttpSafetyReason
{
    /// <summary>The text is not an absolute http or https URL.</summary>
    InvalidUrl,

    /// <summary>The URL embeds user information (<c>user:password@</c>), which is refused everywhere.</summary>
    UserInfoInUrl,

    /// <summary>The host name does not resolve, so its addresses cannot be checked, and the request is not sent.</summary>
    HostNotResolved,

    /// <summary>A host resolves to an address that is not public. Refused before the request, or at connect time.</summary>
    NonPublicAddress,

    /// <summary>The scheme is not allowed here: not http or https, or plain http where https is required.</summary>
    SchemeNotAllowed,

    /// <summary>A redirect takes an https address to plain http. Refused for every request.</summary>
    SchemeDowngrade,

    /// <summary>A request that carries a credential is not https, so the credential is not sent.</summary>
    CredentialNotHttps,

    /// <summary>A request that carries a credential is redirected to another host, so the credential is not sent there.</summary>
    CredentialOffHost,

    /// <summary>A request that carries a credential is redirected to another port, so the credential is not sent there.</summary>
    CredentialOtherPort,

    /// <summary>A 307 or 308 redirect would re-send a request body to another host, so the body is not sent there.</summary>
    BodyOffHost,

    /// <summary>The redirect chain is longer than the allowed number of redirects.</summary>
    TooManyRedirects,

    /// <summary>A redirect status came without a Location header, so there is no next address to follow.</summary>
    MissingLocation,

    /// <summary>A Location header is not a URL that can be used.</summary>
    InvalidLocation,
}
