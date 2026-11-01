// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;

namespace Ballast.Http;

/// <summary>
/// One fetch's result. When <see cref="Refusal"/> is null the page was read: <see cref="Status"/> is what the host answered,
/// <see cref="Body"/> is the body when it answered 2xx (empty otherwise), and <see cref="FinalUrl"/> is the last URL the fetch
/// reached after its redirects. When it is set, nothing was read, and <see cref="Detail"/> says why in words. For a
/// <see cref="PoliteRefusal.CrawlDelayTooLong"/> refusal, <see cref="CrawlDelayAsked"/> is the delay the host asked for.
/// </summary>
public sealed record PoliteFetchResult(
    PoliteRefusal? Refusal,
    string Detail,
    HttpStatusCode? Status,
    Uri FinalUrl,
    byte[] Body,
    TimeSpan? CrawlDelayAsked = null)
{
    /// <summary>True when the page was not fetched.</summary>
    public bool Refused => Refusal is not null;

    /// <summary>The body read as UTF-8 text.</summary>
    public string Text => Encoding.UTF8.GetString(Body);
}
