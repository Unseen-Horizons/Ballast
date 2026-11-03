// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;

namespace Ballast.AppUpdates;

/// <summary>
/// Reads the build settings from configuration, in the shape the apps are set up with:
/// <c>{SectionName}:{Platform}:LatestBuild</c>, <c>MinimumBuild</c>, <c>LatestVersion</c>, <c>StoreUrl</c> and <c>Messages:{language}</c>,
/// and the same keys under <c>{SectionName}:{Platform}:{channel}</c>. That shape is in use by installed builds, so it does not change.
/// <para>
/// A channel's section answers when it has numbers (<c>LatestBuild</c> or <c>MinimumBuild</c>); otherwise the platform-wide section
/// answers, so a build that sends no channel keeps working. The two sections are never merged key by key: a channel that has numbers
/// has its own <c>StoreUrl</c> and messages too, or none.
/// </para>
/// <para>
/// The configuration is read on every call, so a settings change applies without a restart of the reader.
/// </para>
/// </summary>
public sealed class AppVersionConfigReader
{
    private readonly IConfiguration _configuration;
    private readonly string _sectionName;

    /// <summary>Creates the reader over a configuration.</summary>
    /// <param name="configuration">The configuration that holds the section.</param>
    /// <param name="options">The settings. Only <see cref="AppUpdateOptions.SectionName"/> is read here. Null uses the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is blank or repeated.</exception>
    public AppVersionConfigReader(IConfiguration configuration, AppUpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var resolved = options ?? new AppUpdateOptions();
        resolved.Validate();
        _configuration = configuration;
        _sectionName = resolved.SectionName;
    }

    /// <summary>
    /// Reads the platform's section, or the channel's when it has numbers. Nothing is checked here: a section with no numbers is returned
    /// as it is, with null values.
    /// </summary>
    /// <param name="platformName">The platform's name as the configuration names it (for example <c>Android</c>).</param>
    /// <param name="channel">The channel, in lower case, or null for the platform-wide section.</param>
    /// <returns>The raw values.</returns>
    /// <exception cref="ArgumentException"><paramref name="platformName"/> is null, empty or blank.</exception>
    public ConfiguredAppVersion Read(string platformName, string? channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformName);

        var section = _configuration.GetSection($"{_sectionName}:{platformName}");
        var label = platformName;
        string? usedChannel = null;
        if (!string.IsNullOrWhiteSpace(channel))
        {
            var channelSection = section.GetSection(channel);
            if (HasBuildNumbers(channelSection))
            {
                section = channelSection;
                label = $"{platformName}:{channel}";
                usedChannel = channel;
            }
        }

        return new ConfiguredAppVersion(
            label,
            usedChannel,
            section["LatestBuild"],
            section["MinimumBuild"],
            section["LatestVersion"],
            section["StoreUrl"],
            ReadMessages(section));
    }

    private static bool HasBuildNumbers(IConfigurationSection section) =>
        !string.IsNullOrWhiteSpace(section["LatestBuild"]) || !string.IsNullOrWhiteSpace(section["MinimumBuild"]);

    private static Dictionary<string, string> ReadMessages(IConfigurationSection section)
    {
        var messages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetSection("Messages").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                messages[child.Key] = child.Value;
            }
        }

        return messages;
    }
}
