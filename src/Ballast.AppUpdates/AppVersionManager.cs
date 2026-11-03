// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Ballast.Errors;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ballast.AppUpdates;

/// <summary>
/// Answers which builds of the app exist and which are still supported.
/// <para>
/// The numbers are configuration, read per request, so a settings change applies after the restart the platform already does.
/// Nothing is stored. A wrong minimum locks every rider out, so a configuration that does not hold together is an error, never
/// repaired by clamping: the client treats a failed check as "show nothing".
/// </para>
/// <para>
/// An install channel may have its own section (see <see cref="AppVersionConfigReader"/>), because a build is installable only
/// through the channel that carries it. An absent channel is the platform-wide answer.
/// </para>
/// </summary>
public sealed class AppVersionManager
{
    private readonly AppVersionConfigReader _reader;
    private readonly ILogger<AppVersionManager> _logger;
    private readonly AppUpdateOptions _options;
    private readonly Dictionary<string, string> _platforms;
    private readonly HashSet<string> _channels;

    /// <summary>Creates the manager over a configuration.</summary>
    /// <param name="configuration">The configuration that holds the build settings.</param>
    /// <param name="logger">Where a misconfiguration and a rejected store link are logged.</param>
    /// <param name="options">The settings, or null for the defaults that the apps ship with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> or <paramref name="logger"/> is null.</exception>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is blank or repeated.</exception>
    public AppVersionManager(IConfiguration configuration, ILogger<AppVersionManager> logger, AppUpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _options = options ?? new AppUpdateOptions();
        _options.Validate();
        _reader = new AppVersionConfigReader(configuration, _options);
        _logger = logger;
        _platforms = _options.Platforms.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        _channels = new HashSet<string>(_options.Channels, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Answers what the server knows about a platform's builds, for an install channel when one is given.</summary>
    /// <param name="platform">The platform, as the options name it (by default <c>android</c>, <c>ios</c> or <c>windows</c>), without case.</param>
    /// <param name="channel">The install channel, or null or blank for the platform-wide answer.</param>
    /// <returns>The answer. Its <see cref="AppVersionResponse.Channel"/> is set only when the channel's own section answered.</returns>
    /// <exception cref="AppException">
    /// 400 with the platform code for a platform the options do not know. 400 with the channel code for a channel they do not know.
    /// 404 with the not-configured code when the platform (or channel) has no build numbers. 500 with the misconfigured code when the
    /// numbers do not hold together: an unparsable number, a latest build below 1, a minimum above the latest, or a minimum below 0.
    /// A store link that is not absolute https is not an error: it is logged as a warning and dropped.
    /// </exception>
    public AppVersionResponse Get(string? platform, string? channel = null)
    {
        if (string.IsNullOrWhiteSpace(platform) || !_platforms.TryGetValue(platform.Trim(), out var name))
        {
            throw AppException.BadRequest(
                _options.PlatformUnknownCode,
                "Unknown platform.",
                $"platform must be one of: {string.Join(", ", _platforms.Keys)}.");
        }

        // An absent channel is the platform-wide answer (every build before channels existed); a present one must be known.
        string? channelName = null;
        if (!string.IsNullOrWhiteSpace(channel))
        {
            var trimmed = channel.Trim();
            if (!_channels.Contains(trimmed))
            {
                throw AppException.BadRequest(
                    _options.ChannelUnknownCode,
                    "Unknown install channel.",
                    $"channel must be one of: {string.Join(", ", _options.Channels)}.");
            }

            channelName = trimmed.ToLowerInvariant();
        }

        var configured = _reader.Read(name, channelName);

        // No numbers at all means this platform (or channel) is not set up yet: the normal state until they are set.
        if (string.IsNullOrWhiteSpace(configured.LatestBuild) && string.IsNullOrWhiteSpace(configured.MinimumBuild))
        {
            throw AppException.NotFound(_options.NotConfiguredCode, "No app version is configured for this platform.");
        }

        // A minimum without a latest build is allowed: a store channel sets only a minimum, because the store itself says
        // whether a newer build exists. Latest 0 then means "the server announces none".
        var latest = 0;
        if (!string.IsNullOrWhiteSpace(configured.LatestBuild)
            && (!TryParseBuild(configured.LatestBuild, out latest) || latest <= 0))
        {
            throw Misconfigured(configured.Label, $"LatestBuild '{configured.LatestBuild}' is not a positive whole number.");
        }

        var minimum = 0;
        if (!string.IsNullOrWhiteSpace(configured.MinimumBuild)
            && (!TryParseBuild(configured.MinimumBuild, out minimum) || minimum < 0))
        {
            throw Misconfigured(configured.Label, $"MinimumBuild '{configured.MinimumBuild}' is not a whole number of 0 or more.");
        }

        // Never clamped: a minimum above the latest build would block every build there is, the newest included.
        if (latest > 0 && minimum > latest)
        {
            throw Misconfigured(configured.Label, $"MinimumBuild {minimum} is above LatestBuild {latest}.");
        }

        return new AppVersionResponse
        {
            Platform = name,
            LatestBuild = latest,
            MinimumBuild = minimum,
            LatestVersion = string.IsNullOrWhiteSpace(configured.LatestVersion) ? null : configured.LatestVersion.Trim(),
            StoreUrl = ReadStoreUrl(configured),
            Channel = configured.Channel,
            Messages = configured.Messages.ToDictionary(m => m.Key, m => m.Value, StringComparer.OrdinalIgnoreCase),
        };
    }

    private string? ReadStoreUrl(ConfiguredAppVersion configured)
    {
        if (string.IsNullOrWhiteSpace(configured.StoreUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(configured.StoreUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            // An absent link is honest; a wrong one sends riders somewhere unintended.
            _logger.LogWarning(
                "{SectionName}:{Section}:StoreUrl is not an absolute https URL; the response carries no store link.",
                _options.SectionName,
                configured.Label);
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static bool TryParseBuild(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private AppException Misconfigured(string label, string reason)
    {
        _logger.LogError("{SectionName}:{Section} is misconfigured: {Reason}", _options.SectionName, label, reason);
        return new AppException(_options.MisconfiguredCode, "The app version configuration is invalid.", 500);
    }
}
