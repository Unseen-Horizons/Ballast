// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>Why a <see cref="PoliteFetcher"/> refused a page or could not reach it. A result carries one when the page was not fetched.</summary>
public enum PoliteRefusal
{
    /// <summary>robots.txt disallows the path, or robots.txt could not be read (RFC 9309 counts that as disallowing everything).</summary>
    RobotsDisallowed,

    /// <summary>
    /// A redirect led where the crawler does not go: to a scheme other than https, to a URL with credentials, to a non-public
    /// address, or past <see cref="PoliteFetcher.MaxRedirects"/> redirects.
    /// </summary>
    RedirectRefused,

    /// <summary>The body is larger than the caller's byte cap: declared as such, or read past it. Nothing is returned.</summary>
    TooLarge,

    /// <summary>The URL's host is not on a public address.</summary>
    NotPublic,

    /// <summary>The URL is not an https URL without credentials.</summary>
    InvalidUrl,

    /// <summary>The host could not be reached.</summary>
    Unreachable,

    /// <summary>The host did not answer in time.</summary>
    TimedOut,

    /// <summary>
    /// robots.txt asks for a <c>Crawl-delay</c> longer than the fetcher's limit (<see cref="PoliteFetcherOptions.MaxCrawlDelay"/>). Nothing
    /// is waited for: the delay asked is on the result (<see cref="PoliteFetchResult.CrawlDelayAsked"/>).
    /// </summary>
    CrawlDelayTooLong,

    /// <summary>
    /// The URL's host is on the fetcher's blocked hosts (<see cref="PoliteFetcherOptions.BlockedHosts"/>), as a page or as a redirect's
    /// target. Nothing is requested from it: not the page and not its robots.txt. The host is refused before its address is looked up.
    /// </summary>
    BlockedHost,
}
