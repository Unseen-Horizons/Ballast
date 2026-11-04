// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace Ballast.Http;

/// <summary>
/// Reads one public page for one crawler, politely. Every request goes through the same rules:
/// <list type="bullet">
/// <item><description>https only, without credentials in the URL. Only the robots.txt fetch may use http.</description></item>
/// <item><description>Public addresses only, checked by <see cref="PublicDestinationGuard"/> for each URL and, in production, at connect time too.</description></item>
/// <item><description>robots.txt first, cached per origin for <see cref="RobotsCacheFor"/> (see <see cref="RobotsTxt"/>). A disallowed path is never fetched.</description></item>
/// <item><description>No automatic redirects. Each hop is checked again (its scheme, credentials, address and robots.txt), at most <see cref="MaxRedirects"/> of them,
/// by the same rules as <see cref="RedirectFollower"/>.</description></item>
/// <item><description>Blocked hosts (the caller's list) are checked first, for the URL and each hop: a host on the list, or under one, is refused
/// with <see cref="PoliteRefusal.BlockedHost"/> before anything is requested from it.</description></item>
/// <item><description>Per host, a request starts at least <see cref="MinimumGap"/> after the previous request to that host ended, or after its
/// <c>Crawl-delay</c> when that is longer, and a longer one is refused (<see cref="PoliteRefusal.CrawlDelayTooLong"/>). The gap covers the body too.</description></item>
/// <item><description>The body is capped by the caller: a declared length or a read past the cap is <see cref="PoliteRefusal.TooLarge"/>.</description></item>
/// </list>
/// The HTTP client is a named one from <see cref="PoliteFetcherExtensions.AddPoliteFetcher"/>, which has no automatic redirects and the
/// public-only connect check. The clock and the wait are injectable, so tests do not sleep.
/// </summary>
public sealed class PoliteFetcher : IDisposable
{
    /// <summary>The most redirects one fetch follows (the same limit for robots.txt).</summary>
    public const int MaxRedirects = 5;

    /// <summary>The least gap between two requests to one host.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(3);

    /// <summary>How long one origin's robots.txt answer is kept.</summary>
    public static readonly TimeSpan RobotsCacheFor = TimeSpan.FromHours(24);

    /// <summary>RFC 9309 asks a crawler to read at least 500 KiB of robots.txt; more than this is cut off and the prefix is read.</summary>
    public const long MaxRobotsBytes = 500L * 1024;

    private const int CopyBufferBytes = 81920;

    /// <summary>Robots.txt may be read over http, and a redirect of it may go to http. A page may not.</summary>
    private static readonly RedirectPolicy PagePolicy = new(RequireHttps: true, AllowSchemeDowngrade: false);

    private static readonly RedirectPolicy RobotsPolicy = new(RequireHttps: false, AllowSchemeDowngrade: true);

    private readonly IHttpClientFactory _httpFactory;
    private readonly string _clientName;
    private readonly TimeProvider _time;
    private readonly HostPacer _pacer;
    private readonly ConcurrentDictionary<string, CachedRobots> _robots = new(StringComparer.Ordinal);
    private readonly PoliteFetcherOptions _options;
    private readonly IReadOnlyList<string> _blockedHosts;

    /// <summary>
    /// A fetcher over the named client. The clock is <see cref="TimeProvider.System"/> unless one is given. The wait between two
    /// requests to one host is <see cref="Task.Delay(TimeSpan, CancellationToken)"/> unless one is given; a test passes its own.
    /// </summary>
    /// <param name="httpFactory">Creates the client. Required.</param>
    /// <param name="clientName">The name the client is registered under. Required.</param>
    /// <param name="options">The product token, the bot-info URL and the limits. Required. See <see cref="PoliteFetcherOptions"/>.</param>
    /// <param name="time">The clock. Null uses the system clock.</param>
    /// <param name="wait">The wait between two requests to one host. Null uses <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    /// <exception cref="ArgumentException">The product token, the bot-info URL or a blocked host is not valid.</exception>
    public PoliteFetcher(
        IHttpClientFactory httpFactory,
        string clientName,
        PoliteFetcherOptions options,
        TimeProvider? time = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(options);
        PoliteFetcherExtensions.Validate(options);

        _httpFactory = httpFactory;
        _clientName = clientName;
        _options = options;
        _blockedHosts = [.. options.BlockedHosts];
        _time = time ?? TimeProvider.System;
        _pacer = new HostPacer(_time, wait ?? ((delay, ct) => Task.Delay(delay, ct)));
    }

    /// <summary>
    /// Stops the fetcher's per-host pacer. A later <see cref="GetAsync"/> throws <see cref="ObjectDisposedException"/>. A fetch
    /// already running finishes. The dependency injection container calls this when a keyed singleton is disposed.
    /// </summary>
    public void Dispose() => _pacer.Dispose();

    /// <summary>
    /// Reads <paramref name="url"/>, or says why not. The body is at most <paramref name="maxBytes"/> bytes. The caller's cancellation
    /// is rethrown; a network failure or a timeout is a result.
    /// </summary>
    /// <param name="url">The page. Required.</param>
    /// <param name="maxBytes">The most bytes the body may hold.</param>
    /// <param name="ct">Cancels the fetch.</param>
    /// <returns>The result, which says whether the page was read or why it was not.</returns>
    public Task<PoliteFetchResult> GetAsync(Uri url, long maxBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        return GetCoreAsync(url, maxBytes, destination: null, ct);
    }

    /// <summary>
    /// Reads <paramref name="url"/> into <paramref name="destination"/> instead of memory, under the same rules and the same cap. On success the
    /// result's <see cref="PoliteFetchResult.Body"/> is empty and the bytes are in the stream. A <see cref="PoliteRefusal.TooLarge"/> refusal may
    /// leave part of the body written to <paramref name="destination"/>, so the caller discards the stream's content. Nothing is written when the
    /// page is refused, or answers with an error status.
    /// </summary>
    /// <param name="url">The page. Required.</param>
    /// <param name="maxBytes">The most bytes the body may hold.</param>
    /// <param name="destination">Receives the body. Required.</param>
    /// <param name="ct">Cancels the fetch.</param>
    /// <returns>The result.</returns>
    public Task<PoliteFetchResult> GetToAsync(Uri url, long maxBytes, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        return GetCoreAsync(url, maxBytes, destination, ct);
    }

    /// <summary>
    /// Whether a host is on a blocked list: it equals an entry, or ends with "." and the entry, ignoring case. A trailing dot on the host or on an
    /// entry (the fully qualified form of the same name) is not part of the name, so it neither gets past the list nor stops an entry matching.
    /// </summary>
    /// <param name="host">The host to test.</param>
    /// <param name="blockedHosts">The bare host names blocked.</param>
    /// <returns>True when the host is blocked.</returns>
    public static bool IsBlockedHost(string host, IReadOnlyList<string> blockedHosts)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(blockedHosts);
        var name = host.TrimEnd('.');
        return blockedHosts.Any(listed =>
        {
            var entry = listed.TrimEnd('.');
            return name.Equals(entry, StringComparison.OrdinalIgnoreCase) || name.EndsWith("." + entry, StringComparison.OrdinalIgnoreCase);
        });
    }

    private async Task<PoliteFetchResult> GetCoreAsync(Uri url, long maxBytes, Stream? destination, CancellationToken ct)
    {
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
        {
            return Refused(PoliteRefusal.InvalidUrl, "A page is fetched over https only.", url);
        }

        if (url.UserInfo.Length > 0)
        {
            return Refused(PoliteRefusal.InvalidUrl, "A URL that embeds credentials (user:password@) is refused.", url);
        }

        var current = url;
        for (var hop = 0; ; hop++)
        {
            if (IsBlocked(current))
            {
                return Refused(PoliteRefusal.BlockedHost, $"{current.Host} is on the blocked hosts, so nothing is requested from it.", current);
            }

            if (PublicRefusal(current) is { } notPublic)
            {
                return Refused(hop == 0 ? PoliteRefusal.NotPublic : PoliteRefusal.RedirectRefused, notPublic, current);
            }

            var robots = await RobotsForAsync(current, ct);
            if (!robots.IsAllowed(current.PathAndQuery))
            {
                return Refused(PoliteRefusal.RobotsDisallowed, robots.Reason ?? $"robots.txt disallows {current.PathAndQuery}.", current);
            }

            if (robots.CrawlDelay is { } asked && asked > _options.MaxCrawlDelay)
            {
                return Refused(
                    PoliteRefusal.CrawlDelayTooLong,
                    $"{current.Host} asks to wait {asked.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s between requests, over the "
                    + $"{_options.MaxCrawlDelay.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s limit. Nothing was fetched.",
                    current,
                    asked);
            }

            var step = await FetchHopAsync(url, current, robots.CrawlDelay, maxBytes, destination, hop, ct);
            if (step.Next is null)
            {
                return step.Result!;
            }

            current = step.Next;
        }
    }

    /// <summary>One hop's outcome: the result when the fetch ends here, or the next URL when the host redirected.</summary>
    private sealed record Step(PoliteFetchResult? Result, Uri? Next);

    /// <summary>The outcome of one robots.txt hop: its answer when the fetch ends here, or the next URL.</summary>
    private sealed record RobotsStep(RobotsTxt? Answer, Uri? Next);

    private async Task<Step> FetchHopAsync(
        Uri origin, Uri current, TimeSpan? crawlDelay, long maxBytes, Stream? destination, int hop, CancellationToken ct)
    {
        var gap = crawlDelay is { } delay && delay > MinimumGap ? delay : MinimumGap;
        try
        {
            return await _pacer.RunAsync(current.Host, gap, async () =>
            {
                var http = _httpFactory.CreateClient(_clientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (RedirectFollower.IsRedirect(response.StatusCode))
                {
                    return RedirectStep(origin, current, response, hop);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new Step(new PoliteFetchResult(null, $"{current.Host} answered {(int)response.StatusCode}.", response.StatusCode, current, []), null);
                }

                if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
                {
                    return new Step(Refused(PoliteRefusal.TooLarge, $"{current.Host} declares {declared} bytes, over the {maxBytes}-byte limit. Nothing was kept.", current), null);
                }

                await using var body = await response.Content.ReadAsStreamAsync(ct);
                if (destination is null)
                {
                    var (bytes, exceeded) = await ReadUpToAsync(body, maxBytes, ct);
                    if (exceeded)
                    {
                        return new Step(Refused(PoliteRefusal.TooLarge, $"The page is larger than the {maxBytes}-byte limit. Nothing was kept.", current), null);
                    }

                    return new Step(new PoliteFetchResult(null, string.Empty, response.StatusCode, current, bytes), null);
                }

                if (!await CopyUpToAsync(body, destination, maxBytes, ct))
                {
                    return new Step(Refused(PoliteRefusal.TooLarge, $"The page is larger than the {maxBytes}-byte limit. The part read was discarded.", current), null);
                }

                return new Step(new PoliteFetchResult(null, string.Empty, response.StatusCode, current, []), null);
            }, ct);
        }
        catch (HttpRequestException ex) when (ex.InnerException is HttpSafetyException refused)
        {
            return new Step(Refused(hop == 0 ? PoliteRefusal.NotPublic : PoliteRefusal.RedirectRefused, refused.Message, current), null);
        }
        catch (HttpRequestException ex)
        {
            return new Step(Refused(PoliteRefusal.Unreachable, $"{current.Host} could not be reached: {ex.Message}", current), null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Step(Refused(PoliteRefusal.TimedOut, $"{current.Host} did not answer within the time limit.", current), null);
        }
    }

    /// <summary>
    /// A page's redirect: its next hop, or the refusal for it. The Location is read first, so a redirect with no usable Location is refused
    /// (the same order as <see cref="RedirectFollower"/>), then the limit, then the shared rules.
    /// </summary>
    private static Step RedirectStep(Uri origin, Uri current, HttpResponseMessage response, int hop)
    {
        try
        {
            var location = RedirectRules.RequireLocation(response, current);
            if (hop >= MaxRedirects)
            {
                return new Step(Refused(PoliteRefusal.RedirectRefused, $"{current.Host} redirected more than {MaxRedirects} times.", current), null);
            }

            return new Step(null, RedirectRules.Next(origin, current, location, response.StatusCode, bodyFollows: false, credentialed: false, PagePolicy));
        }
        catch (HttpSafetyException ex)
        {
            return new Step(Refused(PoliteRefusal.RedirectRefused, ex.Message, current), null);
        }
    }

    /// <summary>The origin's robots.txt: from the cache while it is fresh, otherwise fetched now.</summary>
    private async Task<RobotsTxt> RobotsForAsync(Uri page, CancellationToken ct)
    {
        var origin = page.GetLeftPart(UriPartial.Authority);
        if (_robots.TryGetValue(origin, out var cached) && _time.GetUtcNow() - cached.FetchedAt < RobotsCacheFor)
        {
            return cached.Rules;
        }

        var rules = await FetchRobotsAsync(new Uri(origin + "/robots.txt"), ct);
        _robots[origin] = new CachedRobots(rules, _time.GetUtcNow());
        return rules;
    }

    /// <summary>
    /// Fetches robots.txt, following its redirects (at most <see cref="MaxRedirects"/>). An unreachable or failing robots.txt is an answer:
    /// everything is disallowed, with the reason. A 4xx is an answer too: everything is allowed (see <see cref="RobotsTxt.FromStatus"/>).
    /// </summary>
    private async Task<RobotsTxt> FetchRobotsAsync(Uri first, CancellationToken ct)
    {
        var current = first;
        for (var hop = 0; ; hop++)
        {
            if (IsBlocked(current))
            {
                return RobotsTxt.DisallowAll($"robots.txt is on a blocked host, {current.Host}, so nothing is requested from it.");
            }

            if (PublicRefusal(current) is { } notPublic)
            {
                return RobotsTxt.DisallowAll($"robots.txt is not on a public address: {notPublic}");
            }

            RobotsStep step;
            try
            {
                step = await _pacer.RunAsync(current.Host, MinimumGap, async () =>
                {
                    var http = _httpFactory.CreateClient(_clientName);
                    using var request = new HttpRequestMessage(HttpMethod.Get, current);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                    if (RedirectFollower.IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
                    {
                        return RobotsRedirectStep(current, location, response.StatusCode, hop);
                    }

                    await using var body = await response.Content.ReadAsStreamAsync(ct);
                    var (bytes, _) = await ReadUpToAsync(body, MaxRobotsBytes, ct);
                    var text = Encoding.UTF8.GetString(bytes);
                    return new RobotsStep(RobotsTxt.FromStatus((int)response.StatusCode, text, _options.ProductToken), null);
                }, ct);
            }
            catch (HttpRequestException ex) when (ex.InnerException is HttpSafetyException refused)
            {
                return RobotsTxt.DisallowAll($"robots.txt is not on a public address: {refused.Message}");
            }
            catch (HttpRequestException ex)
            {
                return RobotsTxt.DisallowAll($"robots.txt could not be fetched: {ex.Message}");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return RobotsTxt.DisallowAll("robots.txt did not answer within the time limit.");
            }

            if (step.Answer is not null)
            {
                return step.Answer;
            }
            current = step.Next!;
        }
    }

    private static RobotsStep RobotsRedirectStep(Uri current, Uri location, HttpStatusCode status, int hop)
    {
        if (hop >= MaxRedirects)
        {
            return new RobotsStep(RobotsTxt.DisallowAll($"robots.txt redirected more than {MaxRedirects} times."), null);
        }

        try
        {
            return new RobotsStep(null, RedirectRules.Next(current, current, location, status, bodyFollows: false, credentialed: false, RobotsPolicy));
        }
        catch (HttpSafetyException)
        {
            return new RobotsStep(RobotsTxt.DisallowAll("robots.txt redirected to a URL that cannot be used."), null);
        }
    }

    /// <summary>Whether the URL's host is on the blocked hosts. Checked before the address, so a blocked host is not even resolved.</summary>
    private bool IsBlocked(Uri uri) => IsBlockedHost(uri.Host, _blockedHosts);

    /// <summary>The reason a URL is not on a public address, or null when it is.</summary>
    private static string? PublicRefusal(Uri uri)
    {
        try
        {
            PublicDestinationGuard.EnsurePublicDestination(uri.AbsoluteUri);
            return null;
        }
        catch (HttpSafetyException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Reads up to <paramref name="limit"/> bytes. The result says whether the body is longer.</summary>
    private static async Task<(byte[] Bytes, bool Exceeded)> ReadUpToAsync(Stream source, long limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[CopyBufferBytes];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > limit)
            {
                return (buffer.ToArray(), true);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        return (buffer.ToArray(), false);
    }

    /// <summary>Copies the body into <paramref name="destination"/>. Answers false, having written nothing more, once the body is longer than <paramref name="limit"/>.</summary>
    private static async Task<bool> CopyUpToAsync(Stream source, Stream destination, long limit, CancellationToken ct)
    {
        var chunk = new byte[CopyBufferBytes];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > limit)
            {
                return false;
            }

            await destination.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        return true;
    }

    private static PoliteFetchResult Refused(PoliteRefusal refusal, string detail, Uri url, TimeSpan? crawlDelayAsked = null) =>
        new(refusal, detail, null, url, [], crawlDelayAsked);

    private sealed record CachedRobots(RobotsTxt Rules, DateTimeOffset FetchedAt);
}
