// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net.Http.Headers;

namespace Ballast.Http;

/// <summary>
/// One request for a <see cref="RedirectFollower"/>: the address asked for first, and what travels with it. A credential, if
/// any, is sent only over https and only to the host and port of <see cref="Url"/>. A body is built again for each hop that
/// re-sends it, because an <see cref="HttpContent"/> cannot be sent twice.
/// </summary>
/// <param name="Url">The absolute address asked for first. Required.</param>
public sealed record RedirectRequest(Uri Url)
{
    /// <summary>The method of the first request. Default GET.</summary>
    public HttpMethod Method { get; init; } = HttpMethod.Get;

    /// <summary>The Authorization header, or null for none. It is sent on every hop that the rules allow, and no other.</summary>
    public AuthenticationHeaderValue? Authorization { get; init; }

    /// <summary>Builds the request body for a hop that sends one, or null for none. GET and HEAD never carry a body.</summary>
    public Func<HttpContent>? Body { get; init; }
}
