// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Json;

using Ballast.Errors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.AppUpdates;

/// <summary>
/// Asks the server which builds exist and which are still supported. The check is advisory: a failure (the network, an error status,
/// bad JSON, a build number that is not usable) is logged and reported as not answered, so a server problem never says anything wrong
/// to a rider. A server that has no build numbers for the platform answers 404, which is the answer "no minimum": it is logged once per
/// client rather than on every ask, and it is an answer, so the last kept answer is replaced.
/// </summary>
public sealed class AppVersionClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AppVersionClient> _logger;
    private readonly string _route;
    private int _notConfiguredLogged;

    /// <summary>Creates the client over an <see cref="HttpClient"/> whose base address is the API's.</summary>
    /// <param name="http">The client to ask with. Its base address is the API's root.</param>
    /// <param name="logger">Where a failed check is logged, or null for none.</param>
    /// <param name="options">The settings. Only <see cref="AppUpdateOptions.Route"/> is read here. Null uses the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="http"/> is null.</exception>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is blank or repeated.</exception>
    public AppVersionClient(HttpClient http, ILogger<AppVersionClient>? logger = null, AppUpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        var resolved = options ?? new AppUpdateOptions();
        resolved.Validate();
        _http = http;
        _logger = logger ?? NullLogger<AppVersionClient>.Instance;
        _route = resolved.Route;
    }

    /// <summary>
    /// Asks the version endpoint for a platform, and for an install channel when one is given. It never throws for a failed call; it
    /// throws only when <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="platform">The platform, as the server names it (by default <c>android</c>, <c>ios</c> or <c>windows</c>).</param>
    /// <param name="channel">The install channel, or null for the platform-wide answer.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>What the server answered. See <see cref="AppVersionAnswer"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="platform"/> is null, empty or blank.</exception>
    public async Task<AppVersionAnswer> GetAsync(string platform, string? channel = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);

        var query = $"platform={Uri.EscapeDataString(platform)}";
        if (!string.IsNullOrWhiteSpace(channel))
        {
            query += $"&channel={Uri.EscapeDataString(channel)}";
        }

        try
        {
            using var response = await _http.GetAsync($"{_route}?{query}", ct);
            if (response.IsSuccessStatusCode)
            {
                var info = await response.Content.ReadFromJsonAsync(AppUpdateJsonContext.Default.AppVersionResponse, ct);
                if (IsUsable(info))
                {
                    return new AppVersionAnswer(true, info);
                }

                _logger.LogWarning("The server's answer carried no usable build number.");
                return default;
            }

            var problem = await ProblemReader.TryReadAsync(response, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // The server has no build numbers for this platform yet: normal until it is configured, so it is logged once.
                // It means "no minimum", not a failure, so the store's answer still decides the banner.
                if (Interlocked.Exchange(ref _notConfiguredLogged, 1) == 0)
                {
                    _logger.LogInformation("404 {Code}: the server has no version for {Platform} ({Channel}) yet.", problem?.Code, platform, channel ?? "no channel");
                }

                return new AppVersionAnswer(true, null);
            }

            _logger.LogWarning(
                "{Status} {Code}: {Detail} (trace {TraceId})",
                (int)response.StatusCode,
                problem?.Code,
                problem?.Detail ?? problem?.Title,
                problem?.TraceId);
            return default;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Offline, a timeout, bad JSON: advisory, so nothing is shown.
            _logger.LogWarning(ex, "Could not reach the version endpoint.");
            return default;
        }
    }

    /// <summary>
    /// An answer carries something to act on: a latest build, or (a store channel) only a minimum. Without either it is not an answer.
    /// </summary>
    private static bool IsUsable(AppVersionResponse? info) => info is { LatestBuild: > 0 } or { MinimumBuild: > 0 };
}
