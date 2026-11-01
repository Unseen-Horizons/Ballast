// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>
/// Options for a <see cref="PoliteFetcher"/>, and for the client registered with <see cref="PoliteFetcherExtensions.AddPoliteFetcher"/>.
/// The product token and the bot-info URL are the app's: they name the crawler to the sites it reads.
/// </summary>
public sealed class PoliteFetcherOptions
{
    /// <summary>
    /// The product token this crawler names in its User-Agent and matches in robots.txt, for example <c>ExampleBot</c>. Letters,
    /// digits, <c>-</c>, <c>_</c> and <c>.</c> only. Required: there is no default name.
    /// </summary>
    public string ProductToken { get; set; } = string.Empty;

    /// <summary>The version of the crawler in its User-Agent, for example <c>1.0</c>. Default <c>1.0</c>.</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// The page that says what the crawler reads and how to block it. It goes into the User-Agent as <c>(+url)</c>. It must be an
    /// absolute https URL. Required.
    /// </summary>
    public string BotInfoUrl { get; set; } = string.Empty;

    /// <summary>
    /// The longest <c>Crawl-delay</c> a host may ask for. A longer one refuses the page with <see cref="PoliteRefusal.CrawlDelayTooLong"/>,
    /// and nothing is slept for it. A shorter one is honoured as the gap between two requests to that host. Default 60 seconds.
    /// </summary>
    public TimeSpan MaxCrawlDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The most time one request may take. Default 60 seconds. Must be above zero.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Hosts the fetcher never requests: a host equal to one of them, or under one. Each is a bare host name (no scheme, slash, port
    /// or space). A page or a redirect on one is refused with <see cref="PoliteRefusal.BlockedHost"/>. Empty blocks nothing.
    /// </summary>
    public IReadOnlyList<string> BlockedHosts { get; set; } = [];
}
