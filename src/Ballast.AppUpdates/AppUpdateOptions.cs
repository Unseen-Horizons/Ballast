// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>
/// The settings of the app's build check: the configuration section, the platforms and install channels it knows, the store
/// channel, the client's route and the error codes. Every default is what the apps ship today, so an app that sets nothing keeps
/// its wire. Set only what differs.
/// </summary>
public sealed class AppUpdateOptions
{
    private static readonly IReadOnlyDictionary<string, string> _defaultPlatforms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["android"] = "Android",
        ["ios"] = "iOS",
        ["windows"] = "Windows",
    };

    private static readonly IReadOnlyList<string> _defaultChannels = [InstallChannels.Play, InstallChannels.Direct, InstallChannels.AppStore];

    /// <summary>
    /// The configuration section the builds are read from. The section holds one child per platform, and a platform may hold one
    /// child per channel: <c>{SectionName}:{Platform}:{channel}:LatestBuild</c>, and so on. Default <c>AppVersion</c>.
    /// </summary>
    public string SectionName { get; init; } = "AppVersion";

    /// <summary>
    /// The platforms the request may name, as the request spells them (matched without case) and as the configuration section names
    /// them. Default: <c>android</c> to <c>Android</c>, <c>ios</c> to <c>iOS</c>, <c>windows</c> to <c>Windows</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Platforms { get; init; } = _defaultPlatforms;

    /// <summary>
    /// The install channels the request may name, matched without case and answered in lower case. Default: <c>play</c>,
    /// <c>direct</c> and <c>appstore</c>.
    /// </summary>
    public IReadOnlyList<string> Channels { get; init; } = _defaultChannels;

    /// <summary>
    /// The install channel whose store is asked whether a newer build exists (the client's <see cref="AppVersionRules.StoreUpdaterFor"/>).
    /// Default <c>play</c>, the one channel that has a store that can say.
    /// </summary>
    public string StoreChannel { get; init; } = InstallChannels.Play;

    /// <summary>
    /// The path the client asks, relative to its base address. Default <c>app/version</c>.
    /// </summary>
    public string Route { get; init; } = "app/version";

    /// <summary>The code of the 400 answer for a platform the server does not know. Default <c>APP_PLATFORM_UNKNOWN</c>.</summary>
    public string PlatformUnknownCode { get; init; } = "APP_PLATFORM_UNKNOWN";

    /// <summary>The code of the 400 answer for an install channel the server does not know. Default <c>APP_CHANNEL_UNKNOWN</c>.</summary>
    public string ChannelUnknownCode { get; init; } = "APP_CHANNEL_UNKNOWN";

    /// <summary>The code of the 404 answer when a platform (or channel) has no build numbers. Default <c>APP_VERSION_NOT_CONFIGURED</c>.</summary>
    public string NotConfiguredCode { get; init; } = "APP_VERSION_NOT_CONFIGURED";

    /// <summary>The code of the 500 answer when the configuration does not hold together. Default <c>APP_VERSION_MISCONFIGURED</c>.</summary>
    public string MisconfiguredCode { get; init; } = "APP_VERSION_MISCONFIGURED";

    /// <summary>
    /// Checks the settings. A blank section name, code or route, an empty platform or channel list, or a platform name that repeats
    /// with a different case is refused, so a bad setting stops the app at startup rather than answering wrongly.
    /// </summary>
    /// <exception cref="ArgumentException">A setting is blank, empty or repeated. The message names it.</exception>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Route);
        ArgumentException.ThrowIfNullOrWhiteSpace(StoreChannel);
        ArgumentException.ThrowIfNullOrWhiteSpace(PlatformUnknownCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(ChannelUnknownCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(NotConfiguredCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(MisconfiguredCode);

        ArgumentNullException.ThrowIfNull(Platforms);
        ArgumentNullException.ThrowIfNull(Channels);
        if (Platforms.Count == 0)
        {
            throw new ArgumentException("At least one platform is needed.", nameof(Platforms));
        }

        if (Channels.Count == 0)
        {
            throw new ArgumentException("At least one install channel is needed.", nameof(Channels));
        }

        foreach (var (name, section) in Platforms)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(section);
        }

        foreach (var channel in Channels)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        }

        // A platform spelled two ways that differ only by case would make one of them unreachable.
        _ = Platforms.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    }
}
